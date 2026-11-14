using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.WebhookSubscriptionDtos;
using MobyGuard.Database.Models;
using MobyGuard.Repos;

namespace MobyGuard.Services;

public class OutboundWebhookDispatcher(
    DatabaseContext context,
    IHttpClientFactory httpClientFactory,
    ILogger<OutboundWebhookDispatcher> logger)
{
    public const string ProviderSlack = "Slack";
    public const string ProviderDiscord = "Discord";
    public const string ProviderTeams = "Teams";

    public async Task<int> DispatchEventAsync(
        string eventKind,
        string actor,
        string repositoryName,
        string referenceOrDetail)
    {
        try
        {
            var enabledSubscriptions = await context.WebhookSubscriptions
                .Where(w => w.IsEnabled)
                .ToListAsync();

            if (enabledSubscriptions.Count == 0)
            {
                return 0;
            }

            var matching = enabledSubscriptions
                .Where(w => MatchesEventKind(w, eventKind) && MatchesRepositoryScope(w.RepositoryPattern, repositoryName))
                .ToList();

            if (matching.Count == 0)
            {
                return 0;
            }

            var (title, summary, eventLabel) = FormatEventMessage(eventKind, actor, repositoryName, referenceOrDetail);
            var deliveredCount = 0;

            foreach (var sub in matching)
            {
                var delivery = await SendSingleAsync(
                    sub,
                    title,
                    summary,
                    actor,
                    string.IsNullOrWhiteSpace(referenceOrDetail) ? repositoryName : $"{repositoryName}:{referenceOrDetail}",
                    eventLabel);

                if (delivery.Success)
                {
                    deliveredCount++;
                }
            }

            await context.SaveChangesAsync();
            return deliveredCount;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error while dispatching outbound {EventKind} webhooks for {Repository}", eventKind, repositoryName);
            return 0;
        }
    }

    public async Task<WebhookDeliveryResultDto> SendSingleAsync(
        WebhookSubscription subscription,
        string title,
        string summary,
        string actor,
        string repositoryScope,
        string eventType)
    {
        var nowUtc = DateTime.UtcNow;
        var payloadJson = BuildPayloadJson(
            subscription.Provider,
            title,
            summary,
            actor,
            repositoryScope,
            eventType,
            nowUtc);

        try
        {
            using var client = httpClientFactory.CreateClient("OutboundWebhook");
            using var content = new StringContent(payloadJson, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            using var response = await client.PostAsync(subscription.TargetUrl, content);

            var statusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync();

            subscription.LastTriggeredAt = nowUtc;
            subscription.LastStatusCode = statusCode;
            subscription.UpdatedAt = nowUtc;

            return new WebhookDeliveryResultDto
            {
                SubscriptionId = subscription.Id,
                Provider = subscription.Provider,
                Success = response.IsSuccessStatusCode,
                StatusCode = statusCode,
                ResponseBody = responseBody.Length > 500 ? responseBody[..500] : responseBody
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbound webhook delivery failed for subscription {Id} ({Provider})", subscription.Id, subscription.Provider);

            subscription.LastTriggeredAt = nowUtc;
            subscription.LastStatusCode = 0;
            subscription.UpdatedAt = nowUtc;

            return new WebhookDeliveryResultDto
            {
                SubscriptionId = subscription.Id,
                Provider = subscription.Provider,
                Success = false,
                StatusCode = 0,
                ResponseBody = ex.Message
            };
        }
    }

    public static string BuildPayloadJson(
        string provider,
        string title,
        string summary,
        string actor,
        string repositoryScope,
        string eventType,
        DateTime timestampUtc)
    {
        var isoTimestamp = timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        if (string.Equals(provider, ProviderDiscord, StringComparison.OrdinalIgnoreCase))
        {
            var color = eventType.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                ? 15158332 // Red (#E74C3C)
                : eventType.Contains("Retention", StringComparison.OrdinalIgnoreCase)
                    ? 3066993 // Green (#2ECC71)
                    : 3447003; // Blue (#3498DB)

            var discordPayload = new
            {
                username = "MobyGuard",
                content = $"**{title}** — {summary}",
                embeds = new[]
                {
                    new
                    {
                        title,
                        description = summary,
                        color,
                        timestamp = isoTimestamp,
                        footer = new { text = "MobyGuard Registry Control Plane" },
                        fields = new[]
                        {
                            new { name = "Actor", value = $"`{actor}`", inline = true },
                            new { name = "Repository / Scope", value = $"`{repositoryScope}`", inline = true },
                            new { name = "Event", value = $"`{eventType}`", inline = true }
                        }
                    }
                }
            };

            return JsonSerializer.Serialize(discordPayload);
        }

        if (string.Equals(provider, ProviderTeams, StringComparison.OrdinalIgnoreCase))
        {
            var teamsPayload = new Dictionary<string, object?>
            {
                ["type"] = "message",
                ["attachments"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["contentType"] = "application/vnd.microsoft.card.adaptive",
                        ["contentUrl"] = null,
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                            ["type"] = "AdaptiveCard",
                            ["version"] = "1.2",
                            ["body"] = new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "TextBlock",
                                    ["text"] = title,
                                    ["weight"] = "bolder",
                                    ["size"] = "large"
                                },
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "TextBlock",
                                    ["text"] = summary,
                                    ["wrap"] = true
                                },
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "FactSet",
                                    ["facts"] = new object[]
                                    {
                                        new { title = "Actor", value = actor },
                                        new { title = "Repository / Scope", value = repositoryScope },
                                        new { title = "Event", value = eventType },
                                        new { title = "Timestamp (UTC)", value = isoTimestamp }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            return JsonSerializer.Serialize(teamsPayload);
        }

        // Default: Slack Incoming Webhook (Root "text" + Block Kit "blocks")
        var slackPayload = new
        {
            text = $"*{title}* — {summary}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = title,
                        emoji = true
                    }
                },
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = summary
                    },
                    fields = new[]
                    {
                        new { type = "mrkdwn", text = $"*Actor:*\n`{actor}`" },
                        new { type = "mrkdwn", text = $"*Repository / Scope:*\n`{repositoryScope}`" },
                        new { type = "mrkdwn", text = $"*Event:*\n`{eventType}`" },
                        new { type = "mrkdwn", text = $"*Timestamp (UTC):*\n`{isoTimestamp}`" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(slackPayload);
    }

    private static bool MatchesEventKind(WebhookSubscription sub, string eventKind) =>
        eventKind.ToLowerInvariant() switch
        {
            "push" => sub.NotifyOnPush,
            "delete" => sub.NotifyOnDelete,
            "retention" => sub.NotifyOnRetention,
            _ => false
        };

    private static bool MatchesRepositoryScope(string subscriptionPattern, string repositoryOrPattern)
    {
        if (subscriptionPattern == "*")
        {
            return true;
        }

        if (string.Equals(subscriptionPattern, repositoryOrPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return RepositoryPermissionRepository.PatternMatchesRepository(subscriptionPattern, repositoryOrPattern);
    }

    private static (string Title, string Summary, string EventLabel) FormatEventMessage(
        string eventKind,
        string actor,
        string repositoryName,
        string referenceOrDetail)
    {
        var target = string.IsNullOrWhiteSpace(referenceOrDetail)
            ? repositoryName
            : $"{repositoryName}:{referenceOrDetail}";

        return eventKind.ToLowerInvariant() switch
        {
            "push" => (
                "MobyGuard: Image Pushed",
                $"User `{actor}` pushed manifest `{target}`.",
                "Manifest_Pushed"),
            "delete" => (
                "MobyGuard: Image Deleted",
                $"User `{actor}` deleted manifest `{target}`.",
                "Manifest_Deleted"),
            "retention" => (
                "MobyGuard: Retention Policy Executed",
                $"Retention policy executed by `{actor}` on scope `{repositoryName}` ({referenceOrDetail}).",
                "Retention_Policy_Executed"),
            _ => (
                "MobyGuard: Registry Event",
                $"Event `{eventKind}` triggered by `{actor}` on `{target}`.",
                eventKind)
        };
    }
}
