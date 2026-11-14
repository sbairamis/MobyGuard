using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Classes;
using MobyGuard.Database.Dtos.UserTypeDtos;
using MobyGuard.Enumerators;

namespace Tests;

public class UserTypeControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public UserTypeControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(PrimaryKeys.UserTypes.Admin, HttpStatusCode.OK)]
    [InlineData(PrimaryKeys.UserTypes.User, HttpStatusCode.OK)]
    [InlineData(PrimaryKeys.UserTypes.Service, HttpStatusCode.Forbidden)]
    public async Task GetAll_Roles_ReturnsExpectedStatus(PrimaryKeys.UserTypes userType, HttpStatusCode expectedStatus)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(userType);

        var response = await client.GetAsync("/api/v1/UserType", TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(PrimaryKeys.UserTypes.Admin, "Admin")]
    [InlineData(PrimaryKeys.UserTypes.User, "User")]
    [InlineData(PrimaryKeys.UserTypes.Service, "Service")]
    public async Task GetById_SystemUserType_ReturnsOk(PrimaryKeys.UserTypes systemUserType, string expectedType)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var response = await client.GetAsync($"/api/v1/UserType/{(int)systemUserType}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<UserTypeDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Equal(expectedType, dto.Type);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var response = await client.GetAsync("/api/v1/UserType/999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.UserTypeNotFound, problem!.Detail);
    }

    [Fact]
    public async Task MutationEndpoints_AreDisabled_ReturnMethodNotAllowed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var postResponse = await client.PostAsJsonAsync("/api/v1/UserType", new { type = "CustomRole" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);

        var putResponse = await client.PutAsJsonAsync("/api/v1/UserType/1", new { type = "ModifiedAdmin" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync("/api/v1/UserType/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteResponse.StatusCode);
    }
}
