using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MobyGuard.Auth;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.AuditLogDtos;
using MobyGuard.Database.Dtos.UserDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Global;
using MobyGuard.Helpers;
using MobyGuard.Pagination;

namespace Tests;

public class UserControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public UserControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<(HttpClient Client, HttpResponseMessage Response)> LoginClientFromAsync(string clientAddress, string username, string password)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
        var response = await client.PostAsJsonAsync(
            "/api/v1/User/login",
            new LoginUserDto { Username = username, Password = password },
            TestContext.Current.CancellationToken);
        return (client, response);
    }

    private Task<HttpResponseMessage> LoginFromAsync(string clientAddress, string username, string password)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
        return client.PostAsJsonAsync(
            "/api/v1/User/login",
            new LoginUserDto { Username = username, Password = password },
            TestContext.Current.CancellationToken);
    }

    private HttpClient CreateClientWithSessionCookie(HttpResponseMessage loginResponse)
    {
        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var sessionSetCookie = setCookies.First(c => c.StartsWith("mobyguard_session=", StringComparison.OrdinalIgnoreCase));
        var cookiePair = sessionSetCookie.Split(';', 2)[0];

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookiePair);
        return client;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndToken()
    {
        var response = await LoginFromAsync("203.0.113.10", "Admin", "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Admin", body.Username);
        Assert.Equal("Admin", body.UserType);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var sessionCookieHeader = setCookies.Single(c => c.StartsWith("mobyguard_session=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);

        var rawCookieValue = Uri.UnescapeDataString(sessionCookieHeader.Split(';', 2)[0]["mobyguard_session=".Length..]);

        using var scope = _factory.Services.CreateScope();
        var sessionCache = scope.ServiceProvider.GetRequiredService<SessionCacheService>();
        var cookieOptions = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        var ticket = cookieOptions.TicketDataFormat.Unprotect(rawCookieValue);
        Assert.NotNull(ticket);
        var jti = ticket.Principal.FindFirst("jti")?.Value;
        Assert.False(string.IsNullOrEmpty(jti));
        Assert.True(sessionCache.IsAccessTokenActive(body.Id, jti!));
    }

    [Fact]
    public async Task DeactivateUser_ImmediatelyRevokesAccessTokenAndRefreshToken()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var userClient = CreateClientWithSessionCookie(loginResponse);

        // Verify active session cookie works
        var meResponseBefore = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponseBefore.StatusCode);

        // Admin deactivates the user
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var updateDto = new UpdateUserDto
        {
            Username = "User",
            UserTypeId = (int)PrimaryKeys.UserTypes.User,
            IsActive = false
        };

        var updateResponse = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
            updateDto,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // The user's active session cookie must immediately be rejected with 401
        var meResponseAfter = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_ImmediatelyRevokesAccessTokenAndRefreshToken()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var userClient = CreateClientWithSessionCookie(loginResponse);

        // Verify active session cookie works
        var meResponseBefore = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponseBefore.StatusCode);

        // Admin deletes the user
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var deleteResponse = await adminClient.DeleteAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // The deleted user's session cookie must immediately fail
        var meResponseAfter = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);
    }

    [Fact]
    public async Task Login_WithServiceUser_ReturnsForbidden()
    {
        // Wrong password on Service account must return 401 InvalidCredentials (no account enumeration)
        var wrongPassResponse = await LoginFromAsync("203.0.113.11", "Service", "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassResponse.StatusCode);
        var wrongPassProblem = await wrongPassResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidCredentials, wrongPassProblem!.Detail);

        // Correct password on Service account reveals 403 ServiceAccountCannotLogin
        var response = await LoginFromAsync("203.0.113.11", "Service", "service");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.ServiceAccountCannotLogin, problem!.Detail);

        // Unknown username also returns 401 for the first LoginMaxRetries attempts and 429 afterwards
        for (var i = 0; i < Variables.LoginMaxRetries; i++)
        {
            var unknownAttempt = await LoginFromAsync("203.0.113.199", "nonexistent_account", "wrong-password");
            Assert.Equal(HttpStatusCode.Unauthorized, unknownAttempt.StatusCode);
        }

        var lockedUnknownAttempt = await LoginFromAsync("203.0.113.199", "nonexistent_account", "wrong-password");
        Assert.Equal(HttpStatusCode.TooManyRequests, lockedUnknownAttempt.StatusCode);
    }

    [Fact]
    public async Task Login_Ipv6AddressesInSameNetwork_ShareLockout()
    {
        for (var i = 0; i < Variables.LoginMaxRetries; i++)
        {
            await LoginFromAsync("2001:db8::1", "User", "wrongpassword");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFromAsync("2001:db8::2", "User", "user")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("2001:db8:0:1::1", "User", "user")).StatusCode);
    }

    [Fact]
    public async Task DeleteById_OwnAccount_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var response = await client.DeleteAsync($"/api/v1/User/{(int)PrimaryKeys.UserTypes.Admin}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotDeleteYourself, problem!.Detail);
    }

    [Fact]
    public async Task SelfServicePasswordChange_ClearsLoginLockoutWithoutRevokingSession()
    {
        // Browser 1 (the legitimate user changing their password)
        var (userClient, loginResponse) = await LoginClientFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        // Browser 2 (another active session for the same user that should be revoked upon password change)
        var (otherSessionClient, otherLoginResponse) = await LoginClientFromAsync("203.0.113.11", "User", "user");
        Assert.Equal(HttpStatusCode.OK, otherLoginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherSessionClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);

        // Lock the account from another IP after login
        for (var i = 0; i < Variables.LoginMaxRetries; i++)
        {
            await LoginFromAsync("198.51.100.77", "User", "wrongpassword");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFromAsync("198.51.100.77", "User", "user")).StatusCode);

        // Self-service password change revokes all old sessions, clears lockouts, and reissues a fresh cookie to the caller
        var updateResponse = await userClient.PutAsJsonAsync(
            "/api/v1/User/me/password",
            new ChangePasswordDto { CurrentPassword = "user", Password = "Val1d#Password99" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // Other existing session must immediately be revoked (401), while current reissued session and cleared lockout IP work
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherSessionClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("198.51.100.77", "User", "Val1d#Password99")).StatusCode);
    }

    [Fact]
    public async Task NonAdminUser_CannotReadGlobalUsersOrAuditLogs()
    {
        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/v1/User", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync($"/api/v1/User/{(int)PrimaryKeys.UserTypes.Admin}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/v1/AuditLog", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/v1/AuditLog/filters", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOkAndHealthy()
    {
        var client = _factory.CreateClient();

        var v1Health = await client.GetAsync("/api/v1/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, v1Health.StatusCode);
        Assert.Equal("Healthy", await v1Health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetupStatus_And_SetupInitialAdmin_OnEmptyDatabase_CreatesAdminAndRejectsSecondSetup()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""DELETE FROM "Users";""", TestContext.Current.CancellationToken);
        }

        var client = _factory.CreateClient();

        var statusBefore = await client.GetFromJsonAsync<SetupStatusDto>("/api/v1/User/setup-status", TestContext.Current.CancellationToken);
        Assert.NotNull(statusBefore);
        Assert.True(statusBefore.SetupRequired);
        Assert.Equal(Variables.AppVersion, statusBefore.ApiVersion);
        Assert.StartsWith("v", statusBefore.ApiVersion, StringComparison.Ordinal);

        var setupTokenService = _factory.Services.GetRequiredService<SetupTokenService>();
        var activeSetupToken = setupTokenService.CurrentToken;
        Assert.False(string.IsNullOrWhiteSpace(activeSetupToken));

        // Step 1: Verify one-time setup token endpoint rejects wrong token and accepts valid token
        var verifyWrongRes = await client.PostAsJsonAsync(
            "/api/v1/User/verify-setup-token",
            new VerifySetupTokenDto { SetupToken = "wrong-token" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, verifyWrongRes.StatusCode);

        var verifyValidRes = await client.PostAsJsonAsync(
            "/api/v1/User/verify-setup-token",
            new VerifySetupTokenDto { SetupToken = activeSetupToken! },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, verifyValidRes.StatusCode);

        // Attempt setup without or with wrong setup token -> 401 Unauthorized
        var invalidTokenResponse = await client.PostAsJsonAsync(
            "/api/v1/User/setup",
            new SetupAdminDto
            {
                Username = "initialadmin",
                Password = "AdminPassword123!",
                SetupToken = "wrong-token"
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidTokenResponse.StatusCode);
        var invalidTokenProblem = await invalidTokenResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidSetupToken, invalidTokenProblem!.Detail);

        var setupResponse = await client.PostAsJsonAsync(
            "/api/v1/User/setup",
            new SetupAdminDto
            {
                Username = "initialadmin",
                Password = "AdminPassword123!",
                SetupToken = activeSetupToken!
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var session = await setupResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.Equal("initialadmin", session.Username);
        Assert.Equal("Admin", session.UserType);
        Assert.Null(setupTokenService.CurrentToken);

        var statusAfter = await client.GetFromJsonAsync<SetupStatusDto>("/api/v1/User/setup-status", TestContext.Current.CancellationToken);
        Assert.NotNull(statusAfter);
        Assert.False(statusAfter.SetupRequired);

        var secondSetupResponse = await client.PostAsJsonAsync(
            "/api/v1/User/setup",
            new SetupAdminDto
            {
                Username = "secondadmin",
                Password = "AdminPassword123!",
                SetupToken = activeSetupToken!
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondSetupResponse.StatusCode);

        var setupAdminClient = CreateClientWithSessionCookie(setupResponse);
        var setupLogs = await setupAdminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            "/api/v1/AuditLog?PageNumber=1&PageSize=20",
            TestContext.Current.CancellationToken);
        Assert.NotNull(setupLogs);
        Assert.Contains(setupLogs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.Setup_Completed)
            && l.Username == "initialadmin"
            && l.ActorUserId == session.Id
            && l.Target == "initialadmin"
            && l.TargetUserId == session.Id
            && !l.Details.Contains(activeSetupToken!));
    }

    [Fact]
    public async Task CreateUser_And_ServiceAccount_And_Logout_RevokesSession()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var createAliceResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/User",
            new AddUserDto
            {
                Username = "alice",
                Password = "AlicePassword123!",
                UserTypeId = (int)PrimaryKeys.UserTypes.User
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createAliceResponse.StatusCode);

        var createBotResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/User",
            new AddUserDto
            {
                Username = "ci-bot",
                Password = "CiBotPassword123!",
                UserTypeId = (int)PrimaryKeys.UserTypes.Service
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createBotResponse.StatusCode);

        var botLogin = await LoginFromAsync("203.0.113.50", "ci-bot", "CiBotPassword123!");
        Assert.Equal(HttpStatusCode.Forbidden, botLogin.StatusCode);

        var aliceLogin = await LoginFromAsync("203.0.113.51", "alice", "AlicePassword123!");
        Assert.Equal(HttpStatusCode.OK, aliceLogin.StatusCode);
        var aliceSession = await aliceLogin.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(aliceSession);

        var aliceClient = CreateClientWithSessionCookie(aliceLogin);

        var logoutResponse = await aliceClient.PostAsync(
            "/api/v1/User/logout",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await aliceClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AuditLog_RecordsEventsAndFilters()
    {
        await LoginFromAsync("203.0.113.99", "User", "user");

        // Failed login must not write an audit entry
        var badLogin = await LoginFromAsync("203.0.113.98", "User", "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var createRes = await adminClient.PostAsJsonAsync(
            "/api/v1/User",
            new AddUserDto
            {
                Username = "audit.target",
                Password = "Audit!Password1",
                UserTypeId = (int)PrimaryKeys.UserTypes.User
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var updateRes = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{created.Id}",
            new UpdateUserDto
            {
                Username = "audit.renamed",
                UserTypeId = (int)PrimaryKeys.UserTypes.Admin,
                IsActive = false
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        var resetRes = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{created.Id}/password",
            new ResetPasswordDto { Password = "Reset!Password2" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, resetRes.StatusCode);

        var (userClient, _) = await LoginClientFromAsync("203.0.113.77", "User", "user");
        var changePwRes = await userClient.PutAsJsonAsync(
            "/api/v1/User/me/password",
            new ChangePasswordDto { CurrentPassword = "user", Password = "Changed!Password3" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, changePwRes.StatusCode);

        var deleteRes = await adminClient.DeleteAsync(
            $"/api/v1/User/{created.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Recreate a different user reusing the old username; UserId search stays isolated by ID across renames & reuse.
        var recreateRes = await adminClient.PostAsJsonAsync(
            "/api/v1/User",
            new AddUserDto
            {
                Username = "audit.target",
                Password = "Audit!Password4",
                UserTypeId = (int)PrimaryKeys.UserTypes.User
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, recreateRes.StatusCode);
        var recreated = await recreateRes.Content.ReadFromJsonAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(recreated);
        Assert.NotEqual(created.Id, recreated.Id);

        var logs = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            "/api/v1/AuditLog?PageNumber=1&PageSize=50",
            TestContext.Current.CancellationToken);
        Assert.NotNull(logs);

        // Failed login IP never appears in the audit log
        Assert.DoesNotContain(logs.Data, l => l.ClientIp == "203.0.113.98");

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.Login_Succeeded)
            && l.Username == "User"
            && l.ActorUserId == (int)PrimaryKeys.UserTypes.User
            && l.ClientIp == "203.0.113.99");

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.User_Created)
            && l.Username == "Admin"
            && l.ActorUserId == (int)PrimaryKeys.UserTypes.Admin
            && l.Target == "audit.target"
            && l.TargetUserId == created.Id
            && l.Details.Contains("role: User"));

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.User_Updated)
            && l.Username == "Admin"
            && l.ActorUserId == (int)PrimaryKeys.UserTypes.Admin
            && l.Target == "audit.renamed"
            && l.TargetUserId == created.Id
            && l.Details.Contains("role: User → Admin")
            && l.Details.Contains("isActive: true → false"));

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.Password_Reset)
            && l.Username == "Admin"
            && l.Target == "audit.renamed"
            && l.TargetUserId == created.Id
            && !l.Details.Contains("Reset!Password2"));

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.Password_Changed)
            && l.Username == "User"
            && l.ActorUserId == (int)PrimaryKeys.UserTypes.User
            && l.Target == "User"
            && l.TargetUserId == (int)PrimaryKeys.UserTypes.User
            && !l.Details.Contains("Changed!Password3"));

        Assert.Contains(logs.Data, l =>
            l.Description == nameof(AuditLogEnums.Description.User_Deleted)
            && l.Username == "Admin"
            && l.Target == "audit.renamed"
            && l.TargetUserId == created.Id);

        // SearchBy.Target (7) and SearchBy.Details (8)
        var targetFiltered = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            $"/api/v1/AuditLog?PageNumber=1&PageSize=20&SearchBy={(int)AuditLogEnums.SearchBy.Target}&ValueStr=audit.renamed",
            TestContext.Current.CancellationToken);
        Assert.NotNull(targetFiltered);
        Assert.True(targetFiltered.TotalRecords >= 3);

        var detailsFiltered = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            $"/api/v1/AuditLog?PageNumber=1&PageSize=20&SearchBy={(int)AuditLogEnums.SearchBy.Details}&ValueStr=isActive",
            TestContext.Current.CancellationToken);
        Assert.NotNull(detailsFiltered);
        Assert.Contains(detailsFiltered.Data, l => l.Description == nameof(AuditLogEnums.Description.User_Updated));

        // SearchBy.UserId (9) links history across renames without leaking a later user's events under the reused name
        var byOldUserId = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            $"/api/v1/AuditLog?PageNumber=1&PageSize=20&SearchBy={(int)AuditLogEnums.SearchBy.UserId}&ValueStr={created.Id}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(byOldUserId);
        Assert.Contains(byOldUserId.Data, l => l.Description == nameof(AuditLogEnums.Description.User_Created) && l.Target == "audit.target");
        Assert.Contains(byOldUserId.Data, l => l.Description == nameof(AuditLogEnums.Description.User_Updated) && l.Target == "audit.renamed");
        Assert.Contains(byOldUserId.Data, l => l.Description == nameof(AuditLogEnums.Description.User_Deleted) && l.Target == "audit.renamed");
        Assert.DoesNotContain(byOldUserId.Data, l => l.TargetUserId == recreated.Id);

        var invalidUserIdSearch = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            $"/api/v1/AuditLog?PageNumber=1&PageSize=20&SearchBy={(int)AuditLogEnums.SearchBy.UserId}&ValueStr=not-a-number",
            TestContext.Current.CancellationToken);
        Assert.NotNull(invalidUserIdSearch);
        Assert.Equal(0, invalidUserIdSearch.TotalRecords);

        var filters = await adminClient.GetFromJsonAsync<AuditLogSearchFiltersDto>(
            "/api/v1/AuditLog/filters",
            TestContext.Current.CancellationToken);
        Assert.NotNull(filters);
        Assert.True(filters.SearchBy.ContainsKey(nameof(AuditLogEnums.SearchBy.Target)));
        Assert.True(filters.SearchBy.ContainsKey(nameof(AuditLogEnums.SearchBy.Details)));
        Assert.True(filters.SearchBy.ContainsKey(nameof(AuditLogEnums.SearchBy.UserId)));
        Assert.True(filters.ValuesStr.Description.ContainsKey(nameof(AuditLogEnums.Description.User_Updated)));
        Assert.True(filters.ValuesStr.Description.ContainsKey(nameof(AuditLogEnums.Description.Webhook_Created)));
        Assert.True(filters.ValuesStr.Description.ContainsKey(nameof(AuditLogEnums.Description.Retention_Policy_Created)));
    }

    [Fact]
    public async Task ChangePassword_And_ResetPassword_UpdatePasswordAndRevokeExistingSessions()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var listResponse = await adminClient.GetAsync("/api/v1/User?PageNumber=1&PageSize=10&Search=User", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var getByIdResponse = await adminClient.GetAsync($"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getByIdResponse.StatusCode);

        var (userClient, userLogin) = await LoginClientFromAsync("203.0.113.55", "User", "user");
        var userSession = await userLogin.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(userSession);

        var wrongCurrentResponse = await userClient.PutAsJsonAsync(
            "/api/v1/User/me/password",
            new ChangePasswordDto { CurrentPassword = "wrong", Password = "New#Password123" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrentResponse.StatusCode);

        var changePasswordResponse = await userClient.PutAsJsonAsync(
            "/api/v1/User/me/password",
            new ChangePasswordDto { CurrentPassword = "user", Password = "New#Password123" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, changePasswordResponse.StatusCode);

        // Own session stays active after self-service password change
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);

        // Login with new password works
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("203.0.113.55", "User", "New#Password123")).StatusCode);

        // Admin resets user's password back, which immediately revokes all active sessions for that user
        var resetPasswordResponse = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}/password",
            new ResetPasswordDto { Password = "Adm1n#ResetPass9" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, resetPasswordResponse.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("203.0.113.55", "User", "Adm1n#ResetPass9")).StatusCode);
    }

    [Fact]
    public async Task PasswordPolicy_And_Blacklist_AreEnforcedAcrossSetupAddChangeAndReset()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Weak passwords violating complexity policy (too short, too long, no uppercase, no digit, no symbol, contains space)
        string[] policyViolations = ["Short1!", $"Aa1!{new string('x', 61)}", "nouppercase1!", "NOLOWERCASE1!", "NoDigitsHere!", "NoSpecialChar123", "Has Space1!"];
        foreach (var weak in policyViolations)
        {
            var res = await adminClient.PostAsJsonAsync(
                "/api/v1/User",
                new AddUserDto
                {
                    Username = "testuser",
                    Password = weak,
                    UserTypeId = (int)PrimaryKeys.UserTypes.User
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.PasswordPolicyViolation, problem!.Detail);
        }

        // 2. Blacklisted passwords (even though they meet complexity rules) and username-derived passwords
        string[] blacklisted = ["Password123!", "Admin123!", "MobyGuard123!", "Welcome123!", "Qwerty123!", "testuser123!"];
        foreach (var banned in blacklisted)
        {
            var res = await adminClient.PostAsJsonAsync(
                "/api/v1/User",
                new AddUserDto
                {
                    Username = "testuser",
                    Password = banned,
                    UserTypeId = (int)PrimaryKeys.UserTypes.User
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.PasswordBlacklisted, problem!.Detail);
        }

        // 3. ResetPassword also rejects blacklisted passwords
        var resetBannedRes = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}/password",
            new ResetPasswordDto { Password = "Password123!" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, resetBannedRes.StatusCode);
    }

    [Fact]
    public async Task RootAdmin_IsProtectedFromSecondaryAdmin_WhileRootAdminCanManageSecondaryAdmins()
    {
        var rootAdminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Root admin (Id == 1) creates a secondary admin
        var createSecondAdminRes = await rootAdminClient.PostAsJsonAsync(
            "/api/v1/User",
            new AddUserDto
            {
                Username = "secondary_admin",
                Password = "SecAdmin!Pass2026#",
                UserTypeId = (int)PrimaryKeys.UserTypes.Admin
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createSecondAdminRes.StatusCode);
        var secondAdmin = await createSecondAdminRes.Content.ReadFromJsonAsync<UserDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(secondAdmin);

        var secondLoginRes = await LoginFromAsync("203.0.113.99", "secondary_admin", "SecAdmin!Pass2026#");
        var secondSession = await secondLoginRes.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(secondSession);

        var secondAdminClient = CreateClientWithSessionCookie(secondLoginRes);

        // 2. Secondary admin cannot delete the root admin (Id == 1)
        var deleteRootRes = await secondAdminClient.DeleteAsync("/api/v1/User/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, deleteRootRes.StatusCode);
        var deleteRootProblem = await deleteRootRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotModifyRootAdmin, deleteRootProblem!.Detail);

        // 3. Secondary admin cannot update, demote, or disable the root admin (Id == 1)
        var updateRootRes = await secondAdminClient.PutAsJsonAsync(
            "/api/v1/User/1",
            new UpdateUserDto
            {
                Username = "hijacked_root",
                UserTypeId = (int)PrimaryKeys.UserTypes.User,
                IsActive = false
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, updateRootRes.StatusCode);
        var updateRootProblem = await updateRootRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotModifyRootAdmin, updateRootProblem!.Detail);

        // 4. Secondary admin cannot reset the root admin's password
        var resetRootRes = await secondAdminClient.PutAsJsonAsync(
            "/api/v1/User/1/password",
            new ResetPasswordDto { Password = "Hijacked!Pass2026#" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, resetRootRes.StatusCode);
        var resetRootProblem = await resetRootRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotModifyRootAdmin, resetRootProblem!.Detail);

        // 5. Root admin CAN demote/disable and delete the secondary admin
        var disableSecondRes = await rootAdminClient.PutAsJsonAsync(
            $"/api/v1/User/{secondAdmin.Id}",
            new UpdateUserDto
            {
                Username = "secondary_admin",
                UserTypeId = (int)PrimaryKeys.UserTypes.User,
                IsActive = false
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, disableSecondRes.StatusCode);

        var deleteSecondRes = await rootAdminClient.DeleteAsync($"/api/v1/User/{secondAdmin.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteSecondRes.StatusCode);
    }

    [Fact]
    public async Task UsernameValidation_And_CaseInsensitiveUniqueness_AreEnforcedAcrossAddUpdateAndConcurrentRequests()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Invalid username formats (colon, backticks, angle brackets, space, leading dot, too short, too long)
        string[] invalidUsernames =
        [
            "a",
            "user:name",
            "`injected`",
            "<http://evil|click>",
            "has space",
            ".leadingdot",
            "-leadingdash",
            new string('a', 65)
        ];

        foreach (var badName in invalidUsernames)
        {
            var addBadRes = await adminClient.PostAsJsonAsync(
                "/api/v1/User",
                new AddUserDto
                {
                    Username = badName,
                    Password = "Valid!Pass2026#",
                    UserTypeId = (int)PrimaryKeys.UserTypes.User
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, addBadRes.StatusCode);
            var problem = await addBadRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.InvalidUsername, problem!.Detail);

            var updateBadRes = await adminClient.PutAsJsonAsync(
                $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
                new UpdateUserDto
                {
                    Username = badName,
                    UserTypeId = (int)PrimaryKeys.UserTypes.User,
                    IsActive = true
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, updateBadRes.StatusCode);
            var updateProblem = await updateBadRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.InvalidUsername, updateProblem!.Detail);
        }

        // 2. Case-insensitive uniqueness on Add ("Admin" already exists)
        foreach (var duplicate in new[] { "Admin", "admin", "ADMIN", "  admin  " })
        {
            var dupRes = await adminClient.PostAsJsonAsync(
                "/api/v1/User",
                new AddUserDto
                {
                    Username = duplicate,
                    Password = "Valid!Pass2026#",
                    UserTypeId = (int)PrimaryKeys.UserTypes.User
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
            var problem = await dupRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.UserExists, problem!.Detail);
        }

        // 3. Case-insensitive uniqueness on Update (cannot rename User #2 to "admin" or "SERVICE")
        foreach (var takenName in new[] { "admin", "ADMIN", "service", "SERVICE" })
        {
            var dupUpdateRes = await adminClient.PutAsJsonAsync(
                $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
                new UpdateUserDto
                {
                    Username = takenName,
                    UserTypeId = (int)PrimaryKeys.UserTypes.User,
                    IsActive = true
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Conflict, dupUpdateRes.StatusCode);
            var problem = await dupUpdateRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ResponseMessages.UserExists, problem!.Detail);
        }

        // Updating a user's own username casing ("User" -> "user" -> "User") is allowed
        var selfCaseUpdate = await adminClient.PutAsJsonAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
            new UpdateUserDto
            {
                Username = "user",
                UserTypeId = (int)PrimaryKeys.UserTypes.User,
                IsActive = true
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, selfCaseUpdate.StatusCode);

        // 4. Concurrent Add requests for the same username (with varying casing) allow only one to succeed
        string[] concurrentCandidates = ["concurrent.user_1", "CONCURRENT.USER_1", "Concurrent.User_1", "concurrent.user_1"];
        var tasks = concurrentCandidates
            .Select(name => adminClient.PostAsJsonAsync(
                "/api/v1/User",
                new AddUserDto
                {
                    Username = name,
                    Password = "Valid!Pass2026#",
                    UserTypeId = (int)PrimaryKeys.UserTypes.User
                },
                TestContext.Current.CancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }
}
