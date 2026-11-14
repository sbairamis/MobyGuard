using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.WebhookSubscriptionDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Services;

namespace MobyGuard.Repos;

public class WebhookSubscriptionRepository(
    DatabaseContext context,
    OutboundWebhookDispatcher dispatcher,
    ILogger<WebhookSubscriptionRepository> logger)
{
    private static readonly string[] SupportedProviders =
    [
        OutboundWebhookDispatcher.ProviderSlack,
        OutboundWebhookDispatcher.ProviderDiscord,
        OutboundWebhookDispatcher.ProviderTeams
    ];

    public async Task<Result<List<WebhookSubscriptionDto>>> GetAll()
    {
        try
        {
            var items = await context.WebhookSubscriptions
                .AsNoTracking()
                .OrderBy(w => w.Id)
                .Select(ToDto)
                .ToListAsync();

            return Result<List<WebhookSubscriptionDto>>.Success(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while listing webhook subscriptions.");
            return Result<List<WebhookSubscriptionDto>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<WebhookSubscriptionDto>> GetById(int id)
    {
        try
        {
            var item = await context.WebhookSubscriptions
                .AsNoTracking()
                .Where(w => w.Id == id)
                .Select(ToDto)
                .FirstOrDefaultAsync();

            if (item is null)
            {
                return Result<WebhookSubscriptionDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.WebhookSubscriptionNotFound);
            }

            return Result<WebhookSubscriptionDto>.Success(item);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching webhook subscription {Id}.", id);
            return Result<WebhookSubscriptionDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<WebhookSubscriptionDto>> Add(SaveWebhookSubscriptionDto dto)
    {
        try
        {
            var error = ValidateDto(dto, out var normalizedName, out var normalizedProvider, out var normalizedUrl, out var normalizedPattern);
            if (error is not null)
            {
                return Result<WebhookSubscriptionDto>.Failure(RepositoryStatus.BadRequest, error);
            }

            if (await context.WebhookSubscriptions.AnyAsync(w => w.Name == normalizedName))
            {
                return Result<WebhookSubscriptionDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.WebhookSubscriptionExists);
            }

            var entity = new WebhookSubscription
            {
                Name = normalizedName,
                Provider = normalizedProvider,
                TargetUrl = normalizedUrl,
                RepositoryPattern = normalizedPattern,
                NotifyOnPush = dto.NotifyOnPush,
                NotifyOnDelete = dto.NotifyOnDelete,
                NotifyOnRetention = dto.NotifyOnRetention,
                IsEnabled = dto.IsEnabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.WebhookSubscriptions.Add(entity);
            await context.SaveChangesAsync();

            return Result<WebhookSubscriptionDto>.Success(MapToDto(entity));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while creating webhook subscription '{Name}'.", dto.Name);
            return Result<WebhookSubscriptionDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Update(int id, SaveWebhookSubscriptionDto dto)
    {
        try
        {
            var entity = await context.WebhookSubscriptions.FindAsync(id);
            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.WebhookSubscriptionNotFound);
            }

            var error = ValidateDto(dto, out var normalizedName, out var normalizedProvider, out var normalizedUrl, out var normalizedPattern);
            if (error is not null)
            {
                return Result.Failure(RepositoryStatus.BadRequest, error);
            }

            if (await context.WebhookSubscriptions.AnyAsync(w => w.Id != id && w.Name == normalizedName))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.WebhookSubscriptionExists);
            }

            entity.Name = normalizedName;
            entity.Provider = normalizedProvider;
            entity.TargetUrl = normalizedUrl;
            entity.RepositoryPattern = normalizedPattern;
            entity.NotifyOnPush = dto.NotifyOnPush;
            entity.NotifyOnDelete = dto.NotifyOnDelete;
            entity.NotifyOnRetention = dto.NotifyOnRetention;
            entity.IsEnabled = dto.IsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating webhook subscription {Id}.", id);
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteById(int id)
    {
        try
        {
            var entity = await context.WebhookSubscriptions.FindAsync(id);
            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.WebhookSubscriptionNotFound);
            }

            context.WebhookSubscriptions.Remove(entity);
            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting webhook subscription {Id}.", id);
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<WebhookDeliveryResultDto>> TestSubscription(int id, int callerId)
    {
        try
        {
            var entity = await context.WebhookSubscriptions.FindAsync(id);
            if (entity is null)
            {
                return Result<WebhookDeliveryResultDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.WebhookSubscriptionNotFound);
            }

            var caller = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId);
            var actorName = caller?.Username ?? "admin";

            var delivery = await dispatcher.SendSingleAsync(
                entity,
                title: "MobyGuard: Test Notification",
                summary: $"Test notification triggered by `{actorName}` for webhook `{entity.Name}` ({entity.Provider}).",
                actor: actorName,
                repositoryScope: entity.RepositoryPattern,
                eventType: "Webhook_Test");

            await context.SaveChangesAsync();

            return Result<WebhookDeliveryResultDto>.Success(delivery);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while testing webhook subscription {Id}.", id);
            return Result<WebhookDeliveryResultDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private static string? ValidateDto(
        SaveWebhookSubscriptionDto dto,
        out string normalizedName,
        out string normalizedProvider,
        out string normalizedUrl,
        out string normalizedPattern)
    {
        normalizedName = dto.Name?.Trim() ?? string.Empty;
        normalizedUrl = dto.TargetUrl?.Trim() ?? string.Empty;
        normalizedPattern = dto.RepositoryPattern?.Trim().ToLowerInvariant() ?? string.Empty;

        var matchedProvider = SupportedProviders.FirstOrDefault(p =>
            string.Equals(p, dto.Provider?.Trim(), StringComparison.OrdinalIgnoreCase));
        normalizedProvider = matchedProvider ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrEmpty(normalizedProvider))
        {
            return ResponseMessages.InvalidWebhookSubscription;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return ResponseMessages.InvalidWebhookSubscription;
        }

        if (!RepositoryPermissionRepository.IsValidPattern(normalizedPattern))
        {
            return ResponseMessages.InvalidRepositoryPattern;
        }

        if (!dto.NotifyOnPush && !dto.NotifyOnDelete && !dto.NotifyOnRetention)
        {
            return ResponseMessages.InvalidWebhookSubscription;
        }

        return null;
    }

    private static WebhookSubscriptionDto MapToDto(WebhookSubscription w) => new()
    {
        Id = w.Id,
        Name = w.Name,
        Provider = w.Provider,
        TargetUrl = w.TargetUrl,
        RepositoryPattern = w.RepositoryPattern,
        NotifyOnPush = w.NotifyOnPush,
        NotifyOnDelete = w.NotifyOnDelete,
        NotifyOnRetention = w.NotifyOnRetention,
        IsEnabled = w.IsEnabled,
        LastTriggeredAt = w.LastTriggeredAt,
        LastStatusCode = w.LastStatusCode,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };

    private static readonly Expression<Func<WebhookSubscription, WebhookSubscriptionDto>> ToDto = w => new WebhookSubscriptionDto
    {
        Id = w.Id,
        Name = w.Name,
        Provider = w.Provider,
        TargetUrl = w.TargetUrl,
        RepositoryPattern = w.RepositoryPattern,
        NotifyOnPush = w.NotifyOnPush,
        NotifyOnDelete = w.NotifyOnDelete,
        NotifyOnRetention = w.NotifyOnRetention,
        IsEnabled = w.IsEnabled,
        LastTriggeredAt = w.LastTriggeredAt,
        LastStatusCode = w.LastStatusCode,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };
}
