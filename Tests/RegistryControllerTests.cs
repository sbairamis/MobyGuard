using System.Net;
using System.Net.Http.Json;
using MobyGuard.Classes;
using MobyGuard.Database.Dtos.RegistryDtos;
using MobyGuard.Database.Dtos.RepositoryPermissionDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Pagination;

namespace Tests;

public class RegistryControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public RegistryControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task GrantUserPullOnlyOnTeamAAsync(HttpClient adminClient)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/RepositoryPermission",
            new AddRepositoryPermissionDto
            {
                UserId = (int)PrimaryKeys.UserTypes.User,
                RepositoryPattern = "team-a/*",
                CanPull = true,
                CanPush = false,
                CanDelete = false
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetRepositories_FiltersCatalogByCallerPermissions()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        await GrantUserPullOnlyOnTeamAAsync(adminClient);

        var adminRepos = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories",
            TestContext.Current.CancellationToken);

        Assert.NotNull(adminRepos);
        Assert.Equal(2, adminRepos.Count);
        Assert.All(adminRepos, r =>
        {
            Assert.True(r.CanPull);
            Assert.True(r.CanPush);
            Assert.True(r.CanDelete);
        });

        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);
        var userRepos = await userClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories",
            TestContext.Current.CancellationToken);

        Assert.NotNull(userRepos);
        Assert.Single(userRepos);
        Assert.Equal("team-a/demo-app", userRepos[0].Name);
        Assert.True(userRepos[0].CanPull);
        Assert.False(userRepos[0].CanPush);
        Assert.False(userRepos[0].CanDelete);
    }

    [Fact]
    public async Task GetTags_And_GetManifest_EnforcesPermissionsAndReturnsMetadata()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        await GrantUserPullOnlyOnTeamAAsync(adminClient);

        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);

        var forbiddenTags = await userClient.GetAsync(
            "/api/v1/Registry/tags?repositoryName=team-b/secret-app",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenTags.StatusCode);

        var tagsDto = await userClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.NotNull(tagsDto);
        Assert.Equal(["v1.0.0"], tagsDto.Tags);
        Assert.False(tagsDto.CanPush);
        Assert.False(tagsDto.CanDelete);

        var manifestDto = await userClient.GetFromJsonAsync<RegistryManifestDto>(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=v1.0.0",
            TestContext.Current.CancellationToken);
        Assert.NotNull(manifestDto);
        Assert.Equal("team-a/demo-app", manifestDto.Repository);
        Assert.Equal("v1.0.0", manifestDto.Reference);
        Assert.Equal(FakeDockerRegistryHandler.DemoDigest, manifestDto.Digest);
        Assert.Equal(4096, manifestDto.TotalSizeBytes);
        Assert.Equal("amd64", manifestDto.Architecture);
        Assert.Equal("linux", manifestDto.Os);
        Assert.Single(manifestDto.Layers);
    }

    [Fact]
    public async Task DeleteManifest_BlocksUserWithoutDeletePermission_AndAllowsAdminWithAuditLog()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        await GrantUserPullOnlyOnTeamAAsync(adminClient);

        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);

        var forbiddenDelete = await userClient.DeleteAsync(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=v1.0.0",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        var adminDelete = await adminClient.DeleteAsync(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=v1.0.0",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.StatusCode);

        var tagsAfter = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.NotNull(tagsAfter);
        Assert.Empty(tagsAfter.Tags);

        var auditLogs = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            "/api/v1/AuditLog?PageNumber=1&PageSize=50",
            TestContext.Current.CancellationToken);
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Data, l => l.Repository == "team-a/demo-app:v1.0.0" && l.Actions == "delete");
    }
}
