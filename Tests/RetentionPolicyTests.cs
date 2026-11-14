using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MobyGuard.Classes;
using MobyGuard.Database.Dtos.RegistryDtos;
using MobyGuard.Database.Dtos.RetentionPolicyDtos;
using MobyGuard.Enumerators;

namespace Tests;

public class RetentionPolicyTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public RetentionPolicyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CrudAndValidation_EnforcesAdminOnly_PatternAndRuleConstraints()
    {
        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);
        var forbiddenGet = await userClient.GetAsync("/api/v1/RetentionPolicy", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenGet.StatusCode);

        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Missing both KeepLastCount and MaxAgeDays -> 400 BadRequest
        var noRuleRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "Invalid No Rule",
                RepositoryPattern = "*",
                KeepLastCount = null,
                MaxAgeDays = null
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, noRuleRes.StatusCode);
        var noRuleProblem = await noRuleRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidRetentionPolicyRule, noRuleProblem!.Detail);

        // 2. Invalid repository pattern -> 400 BadRequest
        var badPatternRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "Bad Pattern Policy",
                RepositoryPattern = "INVALID//PATTERN**",
                KeepLastCount = 5
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badPatternRes.StatusCode);
        var badPatternProblem = await badPatternRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidRepositoryPattern, badPatternProblem!.Detail);

        // 3. Valid creation -> 201 Created
        var createRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "Team-A CI Retention",
                RepositoryPattern = "team-a/*",
                KeepLastCount = 2,
                MaxAgeDays = 30,
                ProtectedTagPatterns = "latest, v*",
                IsEnabled = true
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<RetentionPolicyDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal("Team-A CI Retention", created.Name);
        Assert.Equal("team-a/*", created.RepositoryPattern);
        Assert.Equal(2, created.KeepLastCount);
        Assert.Equal(30, created.MaxAgeDays);
        Assert.Equal("latest,v*", created.ProtectedTagPatterns);

        // 4. Duplicate policy name -> 409 Conflict
        var dupRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "team-a ci retention",
                RepositoryPattern = "*",
                KeepLastCount = 5
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);

        // 5. Update policy -> 204 NoContent
        var updateRes = await adminClient.PutAsJsonAsync(
            $"/api/v1/RetentionPolicy/{created.Id}",
            new SaveRetentionPolicyDto
            {
                Name = "Team-A Updated Policy",
                RepositoryPattern = "team-a/*",
                KeepLastCount = 3,
                MaxAgeDays = null,
                ProtectedTagPatterns = "latest",
                IsEnabled = false
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        var list = await adminClient.GetFromJsonAsync<List<RetentionPolicyDto>>(
            "/api/v1/RetentionPolicy",
            TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("Team-A Updated Policy", list[0].Name);
        Assert.Equal(3, list[0].KeepLastCount);
        Assert.Null(list[0].MaxAgeDays);
        Assert.False(list[0].IsEnabled);

        // 6. Delete policy -> 204 NoContent
        var deleteRes = await adminClient.DeleteAsync(
            $"/api/v1/RetentionPolicy/{created.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
    }

    [Fact]
    public async Task ExecutePolicy_DryRunAndLive_EnforcesProtectedTags_KeepLastCount_AndSharedDigestSafety()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // Seed tags in team-a/demo-app:
        // - v1.0.0 (already present, digest = DemoDigest, protected by "v*")
        // - build-shared (old build that shares DemoDigest with v1.0.0 -> must be SkippedSharedDigest!)
        // - build-3 (newest non-protected build -> Kept because KeepLastCount = 1)
        // - build-2 (older non-protected build with unique digest -> Deleted!)
        // - build-1 (oldest non-protected build with unique digest -> Deleted!)
        const string digestBuild3 = "sha256:b333333333333333333333333333333333333333333333333333333333333333";
        const string digestBuild2 = "sha256:b222222222222222222222222222222222222222222222222222222222222222";
        const string digestBuild1 = "sha256:b111111111111111111111111111111111111111111111111111111111111111";

        _factory.RegistryHandler.SetDemoTagManifest("build-3", digestBuild3, "2026-11-14T15:00:00Z");
        _factory.RegistryHandler.SetDemoTagManifest("build-2", digestBuild2, "2026-11-12T10:00:00Z");
        _factory.RegistryHandler.SetDemoTagManifest("build-1", digestBuild1, "2026-11-10T10:00:00Z");
        _factory.RegistryHandler.SetDemoTagManifest("build-shared", FakeDockerRegistryHandler.DemoDigest, "2026-11-01T10:00:00Z");

        var createRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "Keep Newest Build Only",
                RepositoryPattern = "team-a/*",
                KeepLastCount = 1,
                MaxAgeDays = null,
                ProtectedTagPatterns = "latest,v*",
                IsEnabled = true
            },
            TestContext.Current.CancellationToken);
        createRes.EnsureSuccessStatusCode();
        var policy = await createRes.Content.ReadFromJsonAsync<RetentionPolicyDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(policy);

        // 1. Dry-run preview: should NOT delete anything in upstream registry
        var dryRunRes = await adminClient.PostAsync(
            $"/api/v1/RetentionPolicy/{policy.Id}/run?dryRun=true",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, dryRunRes.StatusCode);
        var dryReport = await dryRunRes.Content.ReadFromJsonAsync<RetentionExecutionResultDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(dryReport);
        Assert.True(dryReport.DryRun);
        Assert.Equal(1, dryReport.MatchedRepositoriesCount);
        Assert.Equal(5, dryReport.EvaluatedTagsCount);
        Assert.Equal(1, dryReport.ProtectedTagsCount); // v1.0.0
        Assert.Equal(2, dryReport.DeletedTagsCount); // build-2, build-1
        Assert.Equal(2, dryReport.DeletedDigestsCount);
        Assert.Equal(1, dryReport.SkippedSharedDigestTagsCount); // build-shared shares DemoDigest with v1.0.0

        // Verify all 5 tags still exist after dry-run
        var tagsAfterDryRun = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app&refresh=true",
            TestContext.Current.CancellationToken);
        Assert.NotNull(tagsAfterDryRun);
        Assert.Equal(5, tagsAfterDryRun.Tags.Count);

        // 2. Live execution: deletes build-2 and build-1, keeps v1.0.0, build-3, and build-shared (since its digest is shared with v1.0.0)
        var liveRunRes = await adminClient.PostAsync(
            $"/api/v1/RetentionPolicy/{policy.Id}/run?dryRun=false",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, liveRunRes.StatusCode);
        var liveReport = await liveRunRes.Content.ReadFromJsonAsync<RetentionExecutionResultDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(liveReport);
        Assert.False(liveReport.DryRun);
        Assert.Equal(2, liveReport.DeletedTagsCount);
        Assert.Equal(2, liveReport.DeletedDigestsCount);
        Assert.Equal(1, liveReport.SkippedSharedDigestTagsCount);

        var tagsAfterLiveRun = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app&refresh=true",
            TestContext.Current.CancellationToken);
        Assert.NotNull(tagsAfterLiveRun);
        Assert.Contains("v1.0.0", tagsAfterLiveRun.Tags);
        Assert.Contains("build-3", tagsAfterLiveRun.Tags);
        Assert.DoesNotContain("build-2", tagsAfterLiveRun.Tags);
        Assert.DoesNotContain("build-1", tagsAfterLiveRun.Tags);

        // Verify policy metadata updated with LastRunAt and LastDeletedTagsCount
        var updatedPolicy = await adminClient.GetFromJsonAsync<RetentionPolicyDto>(
            $"/api/v1/RetentionPolicy/{policy.Id}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(updatedPolicy);
        Assert.NotNull(updatedPolicy.LastRunAt);
        Assert.Equal(2, updatedPolicy.LastDeletedTagsCount);
    }

    [Fact]
    public async Task ScheduledExecution_AutomaticallyRunsDuePolicies_AndSkipsRecentlyRunPolicies()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        const string digestKeep = "sha256:c222222222222222222222222222222222222222222222222222222222222222";
        const string digestDrop = "sha256:c111111111111111111111111111111111111111111111111111111111111111";

        _factory.RegistryHandler.SetDemoTagManifest("scheduled-keep", digestKeep, "2026-11-14T15:00:00Z");
        _factory.RegistryHandler.SetDemoTagManifest("scheduled-drop", digestDrop, "2026-11-10T10:00:00Z");

        var createRes = await adminClient.PostAsJsonAsync(
            "/api/v1/RetentionPolicy",
            new SaveRetentionPolicyDto
            {
                Name = "Daily Scheduled Cleanup",
                RepositoryPattern = "team-a/*",
                KeepLastCount = 1,
                ProtectedTagPatterns = "v*",
                RunIntervalHours = 24,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<RetentionPolicyDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal(24, created.RunIntervalHours);
        Assert.NotNull(created.NextRunAt);

        using (var scope = _factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<MobyGuard.Repos.RetentionPolicyRepository>();

            // First scheduled tick: LastRunAt is null, so policy is due and executes automatically
            var firstCount = await repo.ExecuteDueScheduledPoliciesAsync();
            Assert.Equal(1, firstCount);

            // Second immediate tick: LastRunAt was just updated, so policy is not due again for 24h
            var secondCount = await repo.ExecuteDueScheduledPoliciesAsync();
            Assert.Equal(0, secondCount);
        }

        var tagsAfterScheduledRun = await adminClient.GetFromJsonAsync<RegistryRepositoryTagsDto>(
            "/api/v1/Registry/tags?repositoryName=team-a/demo-app&refresh=true",
            TestContext.Current.CancellationToken);
        Assert.NotNull(tagsAfterScheduledRun);
        Assert.Contains("scheduled-keep", tagsAfterScheduledRun.Tags);
        Assert.DoesNotContain("scheduled-drop", tagsAfterScheduledRun.Tags);
    }
}
