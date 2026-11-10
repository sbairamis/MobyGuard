using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Dtos.UserDtos;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Global;

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
