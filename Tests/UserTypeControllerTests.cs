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

        var response = await client.GetAsync("/api/UserType", TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(PrimaryKeys.UserTypes.Admin)]
    [InlineData(PrimaryKeys.UserTypes.User)]
    [InlineData(PrimaryKeys.UserTypes.Service)]
    public async Task DeleteById_SystemUserType_ReturnsConflict(PrimaryKeys.UserTypes systemUserType)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var response = await client.DeleteAsync($"/api/UserType/{(int)systemUserType}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.CannotDeleteSystemUserType, problem!.Detail);
    }

    [Fact]
    public async Task Add_And_Delete_CustomUserType_Works()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var addResponse = await client.PostAsJsonAsync(
            "/api/UserType",
            new CreateUserTypeDto { Type = "Auditor" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var created = await addResponse.Content.ReadFromJsonAsync<UserTypeDto>(cancellationToken: TestContext.Current.CancellationToken);

        var deleteResponse = await client.DeleteAsync($"/api/UserType/{created!.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
