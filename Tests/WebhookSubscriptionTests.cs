using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Classes;
using MobyGuard.Database.Dtos.RegistryDtos;
using MobyGuard.Database.Dtos.WebhookSubscriptionDtos;
using MobyGuard.Enumerators;

namespace Tests;

public class WebhookSubscriptionTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public WebhookSubscriptionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CrudAndValidation_EnforcesAdminOnly_ProviderAndUrlConstraints()
    {
        var userClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.User);
        var forbiddenRes = await userClient.GetAsync("/api/v1/WebhookSubscription", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);

        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Unsupported provider -> 400 BadRequest
        var badProviderRes = await adminClient.PostAsJsonAsync(
            "/api/v1/WebhookSubscription",
            new SaveWebhookSubscriptionDto
            {
                Name = "Invalid Provider",
                Provider = "UnsupportedApp",
                TargetUrl = "https://hooks.slack.com/services/T00/B00/X00",
                RepositoryPattern = "*"
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badProviderRes.StatusCode);
        var badProviderProblem = await badProviderRes.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidWebhookSubscription, badProviderProblem!.Detail);

        // 2. Create Slack webhook -> 201 Created
        var createRes = await adminClient.PostAsJsonAsync(
            "/api/v1/WebhookSubscription",
            new SaveWebhookSubscriptionDto
            {
                Name = "DevOps Slack Alerts",
                Provider = "Slack",
                TargetUrl = "https://hooks.slack.com/services/T00/B00/X00",
                RepositoryPattern = "team-a/*",
                NotifyOnPush = true,
                NotifyOnDelete = true,
                NotifyOnRetention = false,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<WebhookSubscriptionDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal("Slack", created.Provider);

        // 3. Update to Discord -> 204 NoContent
        var updateRes = await adminClient.PutAsJsonAsync(
            $"/api/v1/WebhookSubscription/{created.Id}",
            new SaveWebhookSubscriptionDto
            {
                Name = "DevOps Discord Alerts",
                Provider = "Discord",
                TargetUrl = "https://discord.com/api/webhooks/12345/abcdef",
                RepositoryPattern = "*",
                NotifyOnPush = true,
                NotifyOnDelete = false,
                NotifyOnRetention = true,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updateRes.StatusCode);

        // 4. Delete -> 204 NoContent
        var deleteRes = await adminClient.DeleteAsync(
            $"/api/v1/WebhookSubscription/{created.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
    }

    [Fact]
    public async Task SlackDiscordAndTeamsFormats_AreVerifiedOnTestAndLivePushEvents()
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);

        // 1. Create Slack, Discord, and Microsoft Teams subscriptions
        var slackSub = await (await adminClient.PostAsJsonAsync(
            "/api/v1/WebhookSubscription",
            new SaveWebhookSubscriptionDto
            {
                Name = "Slack Channel",
                Provider = "Slack",
                TargetUrl = "https://hooks.slack.com/services/T111/B222/S333",
                RepositoryPattern = "team-a/*",
                NotifyOnPush = true,
                NotifyOnDelete = true,
                NotifyOnRetention = true,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<WebhookSubscriptionDto>(cancellationToken: TestContext.Current.CancellationToken);

        var discordSub = await (await adminClient.PostAsJsonAsync(
            "/api/v1/WebhookSubscription",
            new SaveWebhookSubscriptionDto
            {
                Name = "Discord Channel",
                Provider = "Discord",
                TargetUrl = "https://discord.com/api/webhooks/999/discord-token",
                RepositoryPattern = "team-a/*",
                NotifyOnPush = true,
                NotifyOnDelete = true,
                NotifyOnRetention = true,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<WebhookSubscriptionDto>(cancellationToken: TestContext.Current.CancellationToken);

        var teamsSub = await (await adminClient.PostAsJsonAsync(
            "/api/v1/WebhookSubscription",
            new SaveWebhookSubscriptionDto
            {
                Name = "Teams Channel",
                Provider = "Teams",
                TargetUrl = "https://prod-00.westus.logic.azure.com/workflows/teams-token",
                RepositoryPattern = "team-a/*",
                NotifyOnPush = true,
                NotifyOnDelete = true,
                NotifyOnRetention = true,
                IsEnabled = true
            },
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<WebhookSubscriptionDto>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(slackSub);
        Assert.NotNull(discordSub);
        Assert.NotNull(teamsSub);

        // 2. Test Slack webhook and verify Slack Block Kit + root text format
        var testSlackRes = await adminClient.PostAsync(
            $"/api/v1/WebhookSubscription/{slackSub.Id}/test",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, testSlackRes.StatusCode);

        var slackReq = Assert.Single(_factory.OutboundWebhookHandler.ReceivedRequests, r => r.Url.Contains("slack.com"));
        using (var slackDoc = JsonDocument.Parse(slackReq.Body))
        {
            Assert.True(slackDoc.RootElement.TryGetProperty("text", out var textProp));
            Assert.Contains("MobyGuard: Test Notification", textProp.GetString());
            Assert.True(slackDoc.RootElement.TryGetProperty("blocks", out var blocksProp));
            Assert.Equal(JsonValueKind.Array, blocksProp.ValueKind);
            Assert.Equal("header", blocksProp[0].GetProperty("type").GetString());
            Assert.Equal("section", blocksProp[1].GetProperty("type").GetString());
        }

        // 3. Test Discord webhook and verify Discord Embeds format
        var testDiscordRes = await adminClient.PostAsync(
            $"/api/v1/WebhookSubscription/{discordSub.Id}/test",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, testDiscordRes.StatusCode);

        var discordReq = Assert.Single(_factory.OutboundWebhookHandler.ReceivedRequests, r => r.Url.Contains("discord.com"));
        using (var discordDoc = JsonDocument.Parse(discordReq.Body))
        {
            Assert.Equal("MobyGuard", discordDoc.RootElement.GetProperty("username").GetString());
            Assert.True(discordDoc.RootElement.TryGetProperty("content", out _));
            var embeds = discordDoc.RootElement.GetProperty("embeds");
            Assert.Equal(JsonValueKind.Array, embeds.ValueKind);
            var firstEmbed = embeds[0];
            Assert.Equal("MobyGuard: Test Notification", firstEmbed.GetProperty("title").GetString());
            Assert.True(firstEmbed.TryGetProperty("color", out _));
            Assert.True(firstEmbed.TryGetProperty("fields", out var fieldsProp));
            Assert.Equal(3, fieldsProp.GetArrayLength());
        }

        // 4. Test Microsoft Teams webhook and verify Adaptive Card v1.2 format
        var testTeamsRes = await adminClient.PostAsync(
            $"/api/v1/WebhookSubscription/{teamsSub.Id}/test",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, testTeamsRes.StatusCode);

        var teamsReq = Assert.Single(_factory.OutboundWebhookHandler.ReceivedRequests, r => r.Url.Contains("logic.azure.com"));
        using (var teamsDoc = JsonDocument.Parse(teamsReq.Body))
        {
            Assert.Equal("message", teamsDoc.RootElement.GetProperty("type").GetString());
            var attachment = teamsDoc.RootElement.GetProperty("attachments")[0];
            Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
            var cardContent = attachment.GetProperty("content");
            Assert.Equal("AdaptiveCard", cardContent.GetProperty("type").GetString());
            Assert.Equal("1.2", cardContent.GetProperty("version").GetString());
            Assert.Equal("http://adaptivecards.io/schemas/adaptive-card.json", cardContent.GetProperty("body").GetArrayLength() > 0
                ? cardContent.GetProperty("$schema").GetString()
                : string.Empty);
        }

        // 5. Trigger a live Registry Push event on team-a/demo-app:v2.0.0 and verify all 3 webhooks receive it
        _factory.OutboundWebhookHandler.Reset();

        var webhookEnvelope = new RegistryEventEnvelopeDto
        {
            Events =
            [
                new RegistryEventItemDto
                {
                    Action = "push",
                    Target = new RegistryEventTargetDto
                    {
                        MediaType = "application/vnd.oci.image.manifest.v1+json",
                        Repository = "team-a/demo-app",
                        Tag = "v2.0.0",
                        Digest = FakeDockerRegistryHandler.DemoDigest
                    },
                    Actor = new RegistryEventActorDto { Name = "Admin" },
                    Request = new RegistryEventRequestDto { Addr = "198.51.100.42:54321" }
                }
            ]
        };

        var pushEventRes = await adminClient.PostAsJsonAsync(
            "/api/v1/Registry/events",
            webhookEnvelope,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pushEventRes.StatusCode);

        Assert.Equal(3, _factory.OutboundWebhookHandler.ReceivedRequests.Count);
        Assert.All(_factory.OutboundWebhookHandler.ReceivedRequests, req =>
        {
            Assert.Contains("team-a/demo-app:v2.0.0", req.Body);
        });
    }
}
