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

    [Fact]
    public async Task CatalogAndTagsCaching_SearchAndForceRefresh_WorksEndToEnd()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var initialRepos = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories",
            TestContext.Current.CancellationToken);
        Assert.NotNull(initialRepos);
        Assert.Equal(2, initialRepos.Count);
        Assert.Equal(1, _factory.RegistryHandler.CatalogRequestCount);

        // Second request hits cache without calling upstream /v2/_catalog
        var cachedRepos = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories",
            TestContext.Current.CancellationToken);
        Assert.NotNull(cachedRepos);
        Assert.Equal(2, cachedRepos.Count);
        Assert.Equal(1, _factory.RegistryHandler.CatalogRequestCount);

        // Search filter works on cached catalog
        var searchedRepos = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories?search=secret",
            TestContext.Current.CancellationToken);
        Assert.NotNull(searchedRepos);
        Assert.Single(searchedRepos);
        Assert.Equal("team-b/secret-app", searchedRepos[0].Name);

        // Add repository in upstream registry; cached call still returns 2, refresh=true returns 3
        _factory.RegistryHandler.AddRepository("team-c/new-service");
        var stillCached = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories",
            TestContext.Current.CancellationToken);
        Assert.Equal(2, stillCached!.Count);

        var refreshedRepos = await adminClient.GetFromJsonAsync<List<RegistryRepositoryDto>>(
            "/api/v1/Registry/repositories?refresh=true",
            TestContext.Current.CancellationToken);
        Assert.NotNull(refreshedRepos);
        Assert.Equal(3, refreshedRepos.Count);
        Assert.Equal(2, _factory.RegistryHandler.CatalogRequestCount);

        // Tags caching and refresh=true
        var initialTags = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.Equal(["v1.0.0"], initialTags!.Tags);
        Assert.Equal(1, _factory.RegistryHandler.TagsRequestCount);

        _factory.RegistryHandler.AddDemoTag("v2.0.0");
        var cachedTags = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.Equal(["v1.0.0"], cachedTags!.Tags);
        Assert.Equal(1, _factory.RegistryHandler.TagsRequestCount);

        var refreshedTags = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app&refresh=true",
            TestContext.Current.CancellationToken);
        Assert.Equal(["v1.0.0", "v2.0.0"], refreshedTags!.Tags);
        Assert.Equal(2, _factory.RegistryHandler.TagsRequestCount);
    }

    [Fact]
    public async Task ReceiveWebhookEvents_RecordsPushAndPullAuditLogs_IgnoresBlobsAndUiActor_AndInvalidatesCache()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // Warm the tags cache
        var initialTags = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.Equal(["v1.0.0"], initialTags!.Tags);

        // Simulate upstream push of v3.0.0 and send Docker Registry notification envelope
        _factory.RegistryHandler.AddDemoTag("v3.0.0");

        var anonymousClient = _factory.CreateClient();
        var envelope = new RegistryEventEnvelopeDto
        {
            Events =
            [
                // 1. Layer blob push (should be ignored)
                new RegistryEventItemDto
                {
                    Id = "evt-blob-1",
                    Action = "push",
                    Target = new RegistryEventTargetDto
                    {
                        MediaType = "application/vnd.docker.image.rootfs.diff.tar.gzip",
                        Repository = "team-a/demo-app",
                        Digest = FakeDockerRegistryHandler.LayerDigest
                    },
                    Actor = new RegistryEventActorDto { Name = "ci-bot" }
                },
                // 2. Internal mobyguard-ui pull (should be ignored)
                new RegistryEventItemDto
                {
                    Id = "evt-ui-1",
                    Action = "pull",
                    Target = new RegistryEventTargetDto
                    {
                        MediaType = "application/vnd.docker.distribution.manifest.v2+json",
                        Repository = "team-a/demo-app",
                        Tag = "v1.0.0"
                    },
                    Actor = new RegistryEventActorDto { Name = "mobyguard-ui" }
                },
                // 3. Real manifest push by ci-bot
                new RegistryEventItemDto
                {
                    Id = "evt-push-1",
                    Action = "push",
                    Target = new RegistryEventTargetDto
                    {
                        MediaType = "application/vnd.docker.distribution.manifest.v2+json",
                        Repository = "team-a/demo-app",
                        Tag = "v3.0.0",
                        Digest = FakeDockerRegistryHandler.DemoDigest
                    },
                    Request = new RegistryEventRequestDto { Addr = "198.51.100.42:54321" },
                    Actor = new RegistryEventActorDto { Name = "ci-bot" }
                },
                // 4. Real manifest pull by alice over IPv6
                new RegistryEventItemDto
                {
                    Id = "evt-pull-1",
                    Action = "pull",
                    Target = new RegistryEventTargetDto
                    {
                        MediaType = "application/vnd.oci.image.manifest.v1+json",
                        Repository = "team-a/demo-app",
                        Tag = "v3.0.0"
                    },
                    Request = new RegistryEventRequestDto { Addr = "[2001:db8::9]:41200" },
                    Actor = new RegistryEventActorDto { Name = "alice" }
                }
            ]
        };

        var webhookRes = await anonymousClient.PostAsJsonAsync(
            "/api/v1/Registry/events",
            envelope,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        var webhookBody = await webhookRes.Content.ReadFromJsonAsync<RegistryWebhookResultDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(webhookBody);
        Assert.Equal(2, webhookBody.ProcessedEvents);

        // Cache was automatically invalidated by the push webhook, so GetTags returns v3.0.0 immediately without ?refresh=true
        var tagsAfterPush = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app",
            TestContext.Current.CancellationToken);
        Assert.Contains("v3.0.0", tagsAfterPush!.Tags);

        var auditLogs = await adminClient.GetFromJsonAsync<PagedResponse<List<AuditLog>>>(
            "/api/v1/AuditLog?PageNumber=1&PageSize=50",
            TestContext.Current.CancellationToken);
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs.Data, l =>
            l.Username == "ci-bot"
            && l.Repository == "team-a/demo-app:v3.0.0"
            && l.Actions == "push"
            && l.Description == nameof(AuditLogEnums.Description.Manifest_Pushed)
            && l.ClientIp == "198.51.100.42");
        Assert.Contains(auditLogs.Data, l =>
            l.Username == "alice"
            && l.Repository == "team-a/demo-app:v3.0.0"
            && l.Actions == "pull"
            && l.Description == nameof(AuditLogEnums.Description.Manifest_Pulled)
            && l.ClientIp == "2001:db8::9");
    }

    [Fact]
    public async Task GetManifest_MultiArchIndex_ResolvesDefaultPlatformAndSupportsPlatformSelection()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        var defaultManifest = await adminClient.GetFromJsonAsync<RegistryManifestDto>(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=multi-arch",
            TestContext.Current.CancellationToken);
        Assert.NotNull(defaultManifest);
        Assert.True(defaultManifest.IsMultiArch);
        Assert.Equal("multi-arch", defaultManifest.Reference);
        Assert.Equal(FakeDockerRegistryHandler.MultiArchIndexDigest, defaultManifest.Digest);
        Assert.Equal(3, defaultManifest.Platforms.Count);

        // Runnable platforms sorted before attestation manifests
        Assert.False(defaultManifest.Platforms[0].IsAttestation);
        Assert.False(defaultManifest.Platforms[1].IsAttestation);
        Assert.True(defaultManifest.Platforms[2].IsAttestation);

        // Default resolved platform is linux/amd64 with its actual layer size (4096 bytes)
        Assert.Equal(FakeDockerRegistryHandler.DemoDigest, defaultManifest.SelectedPlatformDigest);
        Assert.Equal("amd64", defaultManifest.Architecture);
        Assert.Equal("linux", defaultManifest.Os);
        Assert.Equal(4096, defaultManifest.TotalSizeBytes);
        Assert.Single(defaultManifest.Layers);

        // Request specific platformDigest (linux/arm64)
        var arm64Manifest = await adminClient.GetFromJsonAsync<RegistryManifestDto>(
            $"/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=multi-arch&platformDigest={Uri.EscapeDataString(FakeDockerRegistryHandler.MultiArchArm64Digest)}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(arm64Manifest);
        Assert.True(arm64Manifest.IsMultiArch);
        Assert.Equal("multi-arch", arm64Manifest.Reference);
        Assert.Equal(FakeDockerRegistryHandler.MultiArchArm64Digest, arm64Manifest.SelectedPlatformDigest);
        Assert.Equal("arm64", arm64Manifest.Architecture);
        Assert.Equal("linux", arm64Manifest.Os);
        Assert.Equal(8192, arm64Manifest.TotalSizeBytes);
        Assert.Single(arm64Manifest.Layers);
        Assert.Equal(["/app/server"], arm64Manifest.Entrypoint);
        Assert.Equal(["5090/tcp"], arm64Manifest.ExposedPorts);

        // Unknown platformDigest returns 404 NotFound
        var unknownPlatformRes = await adminClient.GetAsync(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=multi-arch&platformDigest=sha256:9999999999999999999999999999999999999999999999999999999999999999",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownPlatformRes.StatusCode);
    }

    [Fact]
    public async Task GarbageCollectionStatus_TracksDeletedManifestsAndAdminAcknowledgeResetsPendingCount()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);

        var statusBefore = await userClient.GetFromJsonAsync<RegistryGarbageCollectionStatusDto>(
            "/api/v1/Registry/garbage-collection",
            TestContext.Current.CancellationToken);
        Assert.NotNull(statusBefore);
        Assert.Equal(0, statusBefore.PendingDeletedManifestsCount);
        Assert.Null(statusBefore.LastDeletedAt);
        Assert.Null(statusBefore.LastAcknowledgedAt);
        Assert.Contains("garbage-collect", statusBefore.RecommendedCommand);

        // Delete a manifest
        var deleteRes = await adminClient.DeleteAsync(
            "/api/v1/Registry/manifest?repositoryName=team-a/demo-app&reference=v1.0.0",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var statusAfterDelete = await userClient.GetFromJsonAsync<RegistryGarbageCollectionStatusDto>(
            "/api/v1/Registry/garbage-collection",
            TestContext.Current.CancellationToken);
        Assert.NotNull(statusAfterDelete);
        Assert.Equal(1, statusAfterDelete.PendingDeletedManifestsCount);
        Assert.NotNull(statusAfterDelete.LastDeletedAt);

        // Standard user cannot acknowledge garbage collection
        var forbiddenAck = await userClient.PostAsync(
            "/api/v1/Registry/garbage-collection/acknowledge",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAck.StatusCode);

        // Admin acknowledges garbage collection
        var ackRes = await adminClient.PostAsync(
            "/api/v1/Registry/garbage-collection/acknowledge",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ackRes.StatusCode);

        var statusAfterAck = await ackRes.Content.ReadFromJsonAsync<RegistryGarbageCollectionStatusDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(statusAfterAck);
        Assert.Equal(0, statusAfterAck.PendingDeletedManifestsCount);
        Assert.Null(statusAfterAck.LastDeletedAt);
        Assert.NotNull(statusAfterAck.LastAcknowledgedAt);
        Assert.Equal("Admin", statusAfterAck.LastAcknowledgedBy);
    }
}
