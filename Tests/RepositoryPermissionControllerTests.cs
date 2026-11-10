using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Pagination;

namespace Tests;

public class RepositoryPermissionControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public RepositoryPermissionControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(PrimaryKeys.UserTypes.Admin, HttpStatusCode.Created)]
    [InlineData(PrimaryKeys.UserTypes.User, HttpStatusCode.Forbidden)]
    [InlineData(PrimaryKeys.UserTypes.Service, HttpStatusCode.Forbidden)]
    public async Task Add_Roles_ReturnsExpectedStatus(PrimaryKeys.UserTypes userType, HttpStatusCode expectedStatus)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(userType);

        var dto = new AddRepositoryPermissionDto
        {
            UserId = (int)PrimaryKeys.UserTypes.User,
            RepositoryPattern = "mobyguard/backend-api",
            CanPull = true,
            CanPush = false,
            CanDelete = false
        };

        var response = await client.PostAsJsonAsync("/api/RepositoryPermission", dto, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);

        if (expectedStatus == HttpStatusCode.Created)
        {
            var created = await response.Content.ReadFromJsonAsync<RepositoryPermissionDto>(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(created);
            Assert.Equal("mobyguard/backend-api", created.RepositoryPattern);
            Assert.True(created.CanPull);
            Assert.False(created.CanPush);
        }
    }

    [Fact]
    public async Task Add_DuplicateUserAndPattern_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var dto = new AddRepositoryPermissionDto
        {
            UserId = (int)PrimaryKeys.UserTypes.User,
            RepositoryPattern = "mobyguard/app",
            CanPull = true
        };

        await client.PostAsJsonAsync("/api/RepositoryPermission", dto, TestContext.Current.CancellationToken);

        var duplicateResponse = await client.PostAsJsonAsync("/api/RepositoryPermission", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.RepositoryPermissionExists, problem!.Detail);
    }

    [Fact]
    public async Task Add_InvalidPattern_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var dto = new AddRepositoryPermissionDto
        {
            UserId = (int)PrimaryKeys.UserTypes.User,
            RepositoryPattern = "invalid//pattern/**",
            CanPull = true
        };

        var response = await client.PostAsJsonAsync("/api/RepositoryPermission", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidRepositoryPattern, problem!.Detail);
    }

    [Fact]
    public async Task Add_NoActionsSelected_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var dto = new AddRepositoryPermissionDto
        {
            UserId = (int)PrimaryKeys.UserTypes.User,
            RepositoryPattern = "mobyguard/app",
            CanPull = false,
            CanPush = false,
            CanDelete = false
        };

        var response = await client.PostAsJsonAsync("/api/RepositoryPermission", dto, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.NoPermissionActionsSelected, problem!.Detail);
    }

    [Fact]
    public async Task Update_And_Delete_UpdatesAndRemovesPermission()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var createResponse = await client.PostAsJsonAsync(
            "/api/RepositoryPermission",
            new AddRepositoryPermissionDto
            {
                UserId = (int)PrimaryKeys.UserTypes.User,
                RepositoryPattern = "mobyguard/app",
                CanPull = true,
                CanPush = false
            },
            TestContext.Current.CancellationToken);

        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryPermissionDto>(cancellationToken: TestContext.Current.CancellationToken);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/RepositoryPermission/{created!.Id}",
            new UpdateRepositoryPermissionDto
            {
                RepositoryPattern = "mobyguard/app-v2",
                CanPull = true,
                CanPush = true,
                CanDelete = false
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var updated = await client.GetFromJsonAsync<RepositoryPermissionDto>(
            $"/api/RepositoryPermission/{created.Id}",
            TestContext.Current.CancellationToken);

        Assert.Equal("mobyguard/app-v2", updated!.RepositoryPattern);
        Assert.True(updated.CanPush);

        var deleteResponse = await client.DeleteAsync($"/api/RepositoryPermission/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDelete = await client.GetAsync($"/api/RepositoryPermission/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task GetAllPaged_FiltersByUserId()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        await client.PostAsJsonAsync(
            "/api/RepositoryPermission",
            new AddRepositoryPermissionDto { UserId = (int)PrimaryKeys.UserTypes.User, RepositoryPattern = "team-a/*", CanPull = true },
            TestContext.Current.CancellationToken);

        await client.PostAsJsonAsync(
            "/api/RepositoryPermission",
            new AddRepositoryPermissionDto { UserId = (int)PrimaryKeys.UserTypes.Service, RepositoryPattern = "team-b/*", CanPull = true },
            TestContext.Current.CancellationToken);

        var paged = await client.GetFromJsonAsync<PagedResponse<List<RepositoryPermissionDto>>>(
            $"/api/RepositoryPermission?UserId={(int)PrimaryKeys.UserTypes.User}&PageNumber=1&PageSize=10",
            TestContext.Current.CancellationToken);

        Assert.NotNull(paged);
        Assert.Equal(1, paged.TotalRecords);
        Assert.Single(paged.Data);
        Assert.Equal("team-a/*", paged.Data[0].RepositoryPattern);
    }
}
