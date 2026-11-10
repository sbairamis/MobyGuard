using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Dtos.UserDtos;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Global;
using StackExchange.Redis;

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
            "/api/User/login",
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

        // Verify keys in Redis use com.mobeguard namespace prefix
        using var scope = _factory.Services.CreateScope();
        var muxer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var server = muxer.GetServers().First(s => !s.IsReplica);
        var keys = server.Keys(pattern: "com.mobeguard:*").Select(k => (string)k!).ToList();
        Assert.NotEmpty(keys);
        Assert.Contains(keys, k => k.StartsWith("com.mobeguard:User:ActiveTokens:"));
        Assert.Contains(keys, k => k.StartsWith("com.mobeguard:User:RefreshTokens:"));
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
            "/api/User/refresh",
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
            "/api/User/refresh",
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
        var meResponseBefore = await userClient.GetAsync("/api/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponseBefore.StatusCode);

        // Admin deactivates the user
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var updateDto = new UpdateUserDto
        {
            Username = "User",
            FirstName = "User",
            LastName = "User",
            UserTypeId = (int)PrimaryKeys.UserTypes.User,
            IsActive = false
        };

        var updateResponse = await adminClient.PutAsJsonAsync(
            $"/api/User/{(int)PrimaryKeys.UserTypes.User}",
            updateDto,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        // The user's 15-minute access token must immediately be rejected with 401
        var meResponseAfter = await userClient.GetAsync("/api/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);

        // Refresh token must also fail immediately
        var refreshResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/User/refresh",
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
        var meResponseBefore = await userClient.GetAsync("/api/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponseBefore.StatusCode);

        // Admin deletes the user
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var deleteResponse = await adminClient.DeleteAsync(
            $"/api/User/{(int)PrimaryKeys.UserTypes.User}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // The deleted user's access token must immediately fail
        var meResponseAfter = await userClient.GetAsync("/api/User/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseAfter.StatusCode);

        // Refresh token must also fail
        var refreshResponse = await _factory.CreateClient().PostAsJsonAsync(
            "/api/User/refresh",
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

        var response = await client.DeleteAsync($"/api/User/{(int)PrimaryKeys.UserTypes.Admin}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotDeleteYourself, problem!.Detail);
    }
}
