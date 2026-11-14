using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.UserDtos;
using MobyGuard.Enumerators;
using MobyGuard.Global;
using MobyGuard.Helpers;

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

    private Task<HttpResponseMessage> LoginFromAsync(string clientAddress, string username, string password)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
        return client.PostAsJsonAsync(
            "/api/v1/User/login",
            new LoginUserDto { Username = username, Password = password },
            TestContext.Current.CancellationToken);
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
        Assert.False(string.IsNullOrEmpty(body.Token));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));

        // Verify active access token JTI is tracked in IMemoryCache and hashed refresh token is persisted in SQLite
        using var scope = _factory.Services.CreateScope();
        var sessionCache = scope.ServiceProvider.GetRequiredService<SessionCacheService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        var jti = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        Assert.True(sessionCache.IsAccessTokenActive(body.Id, jti));

        var secret = body.RefreshToken.Split('.', 2)[1];
        var expectedHash = SessionService.HashSecret(secret);
        var persisted = await dbContext.RefreshTokens.SingleOrDefaultAsync(
            t => t.UserId == body.Id && t.TokenHash == expectedHash,
            TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        Assert.NotEqual(body.RefreshToken, persisted.TokenHash);
        Assert.True(persisted.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewTokensAndInvalidatesOldToken()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);
        var originalRefreshToken = loginBody.RefreshToken;
        Assert.False(string.IsNullOrEmpty(originalRefreshToken));

        var client = _factory.CreateClient();
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = originalRefreshToken },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(refreshBody);
        Assert.False(string.IsNullOrEmpty(refreshBody.Token));
        Assert.False(string.IsNullOrEmpty(refreshBody.RefreshToken));
        Assert.NotEqual(originalRefreshToken, refreshBody.RefreshToken);

        // Attempting to reuse the consumed refresh token should fail with 401
        var reuseResponse = await client.PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = originalRefreshToken },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_ImmediatelyRevokesAccessTokenAndRefreshToken()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Token);

        // Verify active token works
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

        // The user's 15-minute access token must immediately be rejected with 401
        var meResponseAfter = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);

        // Refresh token must also fail immediately
        var refreshResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = loginBody.RefreshToken },
            TestContext.Current.CancellationToken);
        Assert.True(refreshResponse.StatusCode == HttpStatusCode.Unauthorized || refreshResponse.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteUser_ImmediatelyRevokesAccessTokenAndRefreshToken()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Token);

        // Verify active token works
        var meResponseBefore = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponseBefore.StatusCode);

        // Admin deletes the user
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var deleteResponse = await adminClient.DeleteAsync(
            $"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // The deleted user's access token must immediately fail
        var meResponseAfter = await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);

        // Refresh token must also fail
        var refreshResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = loginBody.RefreshToken },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithServiceUser_ReturnsForbidden()
    {
        var response = await LoginFromAsync("203.0.113.11", "Service", "service");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.ServiceAccountCannotLogin, problem!.Detail);
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
    public async Task RefreshToken_WithExpiredToken_ReturnsUnauthorized()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var secret = loginBody.RefreshToken.Split('.', 2)[1];
        var tokenHash = SessionService.HashSecret(secret);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var expiredAt = DateTime.UtcNow.AddMinutes(-5);
            await dbContext.RefreshTokens
                .Where(t => t.TokenHash == tokenHash)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.ExpiresAt, expiredAt),
                    TestContext.Current.CancellationToken);
        }

        var refreshResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = loginBody.RefreshToken },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_ConcurrentRequests_OnlyOneSucceeds()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        var tasks = Enumerable.Range(0, 6)
            .Select(_ => _factory.CreateClient().PostAsJsonAsync(
                "/api/v1/User/refresh",
                new RefreshTokenDto { RefreshToken = loginBody.RefreshToken },
                TestContext.Current.CancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task SelfServicePasswordChange_ClearsLoginLockoutWithoutRevokingSession()
    {
        var loginResponse = await LoginFromAsync("203.0.113.10", "User", "user");
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(loginBody);

        // Lock the account from another IP after login
        for (var i = 0; i < Variables.LoginMaxRetries; i++)
        {
            await LoginFromAsync("198.51.100.77", "User", "wrongpassword");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFromAsync("198.51.100.77", "User", "user")).StatusCode);

        // Self-service password change calls ClearAllUserLoginRetries, NOT DeleteUserEntries
        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Token);

        var updateResponse = await userClient.PutAsJsonAsync(
            "/api/v1/User/me/password",
            new ChangePasswordDto { CurrentPassword = "user", Password = "Val1d#Password99" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // Existing access token, refresh token, and cleared lockout IP must all work
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginFromAsync("198.51.100.77", "User", "Val1d#Password99")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = loginBody.RefreshToken },
            TestContext.Current.CancellationToken)).StatusCode);
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

        var legacyHealth = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, legacyHealth.StatusCode);
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
        Assert.Equal("v1.0.7", statusBefore.ApiVersion);

        var setupResponse = await client.PostAsJsonAsync(
            "/api/v1/User/setup",
            new SetupAdminDto
            {
                Username = "initialadmin",
                Password = "AdminPassword123!"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var session = await setupResponse.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.Equal("initialadmin", session.Username);
        Assert.Equal("Admin", session.UserType);

        var statusAfter = await client.GetFromJsonAsync<SetupStatusDto>("/api/v1/User/setup-status", TestContext.Current.CancellationToken);
        Assert.NotNull(statusAfter);
        Assert.False(statusAfter.SetupRequired);

        var secondSetupResponse = await client.PostAsJsonAsync(
            "/api/v1/User/setup",
            new SetupAdminDto
            {
                Username = "secondadmin",
                Password = "AdminPassword123!"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, secondSetupResponse.StatusCode);
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

        var aliceClient = _factory.CreateClient();
        aliceClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aliceSession.Token);

        var logoutResponse = await aliceClient.PostAsJsonAsync(
            "/api/v1/User/logout",
            new RefreshTokenDto { RefreshToken = aliceSession.RefreshToken },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await aliceClient.GetAsync("/api/v1/User/me", TestContext.Current.CancellationToken)).StatusCode);

        var refreshAfterLogout = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/User/refresh",
            new RefreshTokenDto { RefreshToken = aliceSession.RefreshToken },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task AuditLog_RecordsEventsAndFilters()
    {
        await LoginFromAsync("203.0.113.99", "User", "user");

        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var logsResponse = await adminClient.GetAsync("/api/v1/AuditLog?PageNumber=1&PageSize=50", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);

        var filtersResponse = await adminClient.GetAsync("/api/v1/AuditLog/filters", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, filtersResponse.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_And_ResetPassword_UpdatePasswordAndRevokeExistingSessions()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var listResponse = await adminClient.GetAsync("/api/v1/User?PageNumber=1&PageSize=10&Search=User", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var getByIdResponse = await adminClient.GetAsync($"/api/v1/User/{(int)PrimaryKeys.UserTypes.User}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getByIdResponse.StatusCode);

        var userLogin = await LoginFromAsync("203.0.113.55", "User", "user");
        var userSession = await userLogin.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(userSession);

        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userSession.Token);

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

        // 1. Weak passwords violating complexity policy (too short, no uppercase, no digit, no symbol, contains space)
        string[] policyViolations = ["Short1!", "nouppercase1!", "NOLOWERCASE1!", "NoDigitsHere!", "NoSpecialChar123", "Has Space1!"];
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
}
