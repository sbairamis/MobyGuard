using System.IO.Enumeration;
using System.Linq.Expressions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.RetentionPolicyDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Services;

namespace MobyGuard.Repos;

public class RetentionPolicyRepository(
    DatabaseContext context,
    RegistryRepository registryRepository,
    OutboundWebhookDispatcher webhookDispatcher,
    ILogger<RetentionPolicyRepository> logger)
{
    public async Task<Result<List<RetentionPolicyDto>>> GetAll()
    {
        try
        {
            var policies = await context.RetentionPolicies
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(ToDto)
                .ToListAsync();

            return Result<List<RetentionPolicyDto>>.Success(policies);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching retention policies.");
            return Result<List<RetentionPolicyDto>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RetentionPolicyDto>> GetById(int id)
    {
        try
        {
            var policy = await context.RetentionPolicies
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(ToDto)
                .FirstOrDefaultAsync();

            if (policy is null)
            {
                return Result<RetentionPolicyDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.RetentionPolicyNotFound);
            }

            return Result<RetentionPolicyDto>.Success(policy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching retention policy {Id}.", id);
            return Result<RetentionPolicyDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RetentionPolicyDto>> Add(SaveRetentionPolicyDto dto)
    {
        try
        {
            var validationError = ValidateDto(dto, out var normalizedName, out var normalizedPattern, out var normalizedProtected);
            if (validationError is not null)
            {
                return Result<RetentionPolicyDto>.Failure(RepositoryStatus.BadRequest, validationError);
            }

            if (await context.RetentionPolicies.AnyAsync(p => p.Name == normalizedName))
            {
                return Result<RetentionPolicyDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.RetentionPolicyExists);
            }

            var entity = new RetentionPolicy
            {
                Name = normalizedName,
                RepositoryPattern = normalizedPattern,
                KeepLastCount = dto.KeepLastCount,
                MaxAgeDays = dto.MaxAgeDays,
                ProtectedTagPatterns = normalizedProtected,
                IsEnabled = dto.IsEnabled,
                RunIntervalHours = dto.RunIntervalHours,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.RetentionPolicies.Add(entity);
            await context.SaveChangesAsync();

            return Result<RetentionPolicyDto>.Success(MapToDto(entity));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while creating retention policy '{Name}'.", dto.Name);
            return Result<RetentionPolicyDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Update(int id, SaveRetentionPolicyDto dto)
    {
        try
        {
            var entity = await context.RetentionPolicies.FindAsync(id);
            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RetentionPolicyNotFound);
            }

            var validationError = ValidateDto(dto, out var normalizedName, out var normalizedPattern, out var normalizedProtected);
            if (validationError is not null)
            {
                return Result.Failure(RepositoryStatus.BadRequest, validationError);
            }

            if (await context.RetentionPolicies.AnyAsync(p => p.Id != id && p.Name == normalizedName))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.RetentionPolicyExists);
            }

            entity.Name = normalizedName;
            entity.RepositoryPattern = normalizedPattern;
            entity.KeepLastCount = dto.KeepLastCount;
            entity.MaxAgeDays = dto.MaxAgeDays;
            entity.ProtectedTagPatterns = normalizedProtected;
            entity.IsEnabled = dto.IsEnabled;
            entity.RunIntervalHours = dto.RunIntervalHours;
            entity.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating retention policy {Id}.", id);
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteById(int id)
    {
        try
        {
            var entity = await context.RetentionPolicies.FindAsync(id);
            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RetentionPolicyNotFound);
            }

            context.RetentionPolicies.Remove(entity);
            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting retention policy {Id}.", id);
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RetentionExecutionResultDto>> ExecutePolicy(
        int id,
        bool dryRun,
        int callerId,
        IPAddress? clientAddress)
    {
        try
        {
            var policy = await context.RetentionPolicies.FindAsync(id);
            if (policy is null)
            {
                return Result<RetentionExecutionResultDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.RetentionPolicyNotFound);
            }

            var caller = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller is null)
            {
                return Result<RetentionExecutionResultDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            var reposResult = await registryRepository.GetRepositories(callerId, forceRefresh: true);
            if (reposResult.Status != RepositoryStatus.Success || reposResult.Data is null)
            {
                return Result<RetentionExecutionResultDto>.Failure(
                    reposResult.Status,
                    !string.IsNullOrEmpty(reposResult.ResponseMessage)
                        ? reposResult.ResponseMessage
                        : ResponseMessages.RegistryUpstreamError);
            }

            var matchingRepos = reposResult.Data
                .Where(r => RepositoryPermissionRepository.PatternMatchesRepository(policy.RepositoryPattern, r.Name))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var protectedPatterns = ParseProtectedPatterns(policy.ProtectedTagPatterns);
            var executionResult = new RetentionExecutionResultDto
            {
                PolicyId = policy.Id,
                PolicyName = policy.Name,
                DryRun = dryRun,
                MatchedRepositoriesCount = matchingRepos.Count
            };

            var nowUtc = DateTime.UtcNow;

            foreach (var repo in matchingRepos)
            {
                var tagsResult = await registryRepository.GetTags(repo.Name, callerId, forceRefresh: true);
                if (tagsResult.Status != RepositoryStatus.Success || tagsResult.Data is null || tagsResult.Data.Tags.Count == 0)
                {
                    continue;
                }

                var tagSnapshots = new List<(string Tag, string Digest, DateTime? CreatedAt, string? MatchedProtectedPattern)>();

                foreach (var tag in tagsResult.Data.Tags)
                {
                    var manifestRes = await registryRepository.GetManifest(repo.Name, tag, callerId);
                    if (manifestRes.Status != RepositoryStatus.Success || manifestRes.Data is null || string.IsNullOrWhiteSpace(manifestRes.Data.Digest))
                    {
                        continue;
                    }

                    var matchedProtected = FindMatchingProtectedPattern(tag, protectedPatterns);
                    tagSnapshots.Add((tag, manifestRes.Data.Digest, manifestRes.Data.CreatedAt, matchedProtected));
                }

                executionResult.EvaluatedTagsCount += tagSnapshots.Count;

                var keptDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidateRemovals = new List<(string Tag, string Digest, DateTime? CreatedAt, string Reason)>();

                // 1. Always keep protected tags
                foreach (var snap in tagSnapshots.Where(s => s.MatchedProtectedPattern is not null))
                {
                    executionResult.ProtectedTagsCount++;
                    keptDigests.Add(snap.Digest);
                    executionResult.Items.Add(new RetentionTagActionDto
                    {
                        Repository = repo.Name,
                        Tag = snap.Tag,
                        Digest = snap.Digest,
                        CreatedAt = snap.CreatedAt,
                        Action = "Kept",
                        Reason = $"Protected by pattern '{snap.MatchedProtectedPattern}'"
                    });
                }

                // 2. Evaluate non-protected tags ordered newest-first
                var nonProtected = tagSnapshots
                    .Where(s => s.MatchedProtectedPattern is null)
                    .OrderByDescending(s => s.CreatedAt ?? DateTime.MinValue)
                    .ThenByDescending(s => s.Tag, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (var index = 0; index < nonProtected.Count; index++)
                {
                    var snap = nonProtected[index];
                    string? expireReason = null;

                    if (policy.KeepLastCount.HasValue && index >= policy.KeepLastCount.Value)
                    {
                        expireReason = $"Exceeded KeepLastCount ({policy.KeepLastCount.Value})";
                    }
                    else if (policy.MaxAgeDays.HasValue
                             && snap.CreatedAt.HasValue
                             && (nowUtc - snap.CreatedAt.Value).TotalDays > policy.MaxAgeDays.Value)
                    {
                        expireReason = $"Older than {policy.MaxAgeDays.Value} day(s)";
                    }

                    if (expireReason is null)
                    {
                        keptDigests.Add(snap.Digest);
                        executionResult.Items.Add(new RetentionTagActionDto
                        {
                            Repository = repo.Name,
                            Tag = snap.Tag,
                            Digest = snap.Digest,
                            CreatedAt = snap.CreatedAt,
                            Action = "Kept",
                            Reason = "Within retention policy limits"
                        });
                    }
                    else
                    {
                        candidateRemovals.Add((snap.Tag, snap.Digest, snap.CreatedAt, expireReason));
                    }
                }

                // 3. Process candidate removals with shared-digest protection
                var deletedDigestsInRepo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var candidate in candidateRemovals)
                {
                    if (keptDigests.Contains(candidate.Digest))
                    {
                        executionResult.SkippedSharedDigestTagsCount++;
                        executionResult.Items.Add(new RetentionTagActionDto
                        {
                            Repository = repo.Name,
                            Tag = candidate.Tag,
                            Digest = candidate.Digest,
                            CreatedAt = candidate.CreatedAt,
                            Action = "SkippedSharedDigest",
                            Reason = $"{candidate.Reason}, but digest is shared with a kept/protected tag"
                        });
                        continue;
                    }

                    if (dryRun)
                    {
                        executionResult.DeletedTagsCount++;
                        if (deletedDigestsInRepo.Add(candidate.Digest))
                        {
                            executionResult.DeletedDigestsCount++;
                        }

                        executionResult.Items.Add(new RetentionTagActionDto
                        {
                            Repository = repo.Name,
                            Tag = candidate.Tag,
                            Digest = candidate.Digest,
                            CreatedAt = candidate.CreatedAt,
                            Action = "WouldDelete",
                            Reason = candidate.Reason
                        });
                    }
                    else
                    {
                        var alreadyDeletedDigest = !deletedDigestsInRepo.Add(candidate.Digest);
                        if (!alreadyDeletedDigest)
                        {
                            var delRes = await registryRepository.DeleteManifest(
                                repo.Name,
                                candidate.Digest,
                                callerId,
                                clientAddress);

                            if (delRes.Status != RepositoryStatus.Success)
                            {
                                deletedDigestsInRepo.Remove(candidate.Digest);
                                continue;
                            }

                            executionResult.DeletedDigestsCount++;
                        }

                        executionResult.DeletedTagsCount++;
                        executionResult.Items.Add(new RetentionTagActionDto
                        {
                            Repository = repo.Name,
                            Tag = candidate.Tag,
                            Digest = candidate.Digest,
                            CreatedAt = candidate.CreatedAt,
                            Action = "Deleted",
                            Reason = candidate.Reason
                        });
                    }
                }
            }

            if (!dryRun)
            {
                policy.LastRunAt = nowUtc;
                policy.LastDeletedTagsCount = executionResult.DeletedTagsCount;
                policy.UpdatedAt = nowUtc;

                context.AuditLogs.Add(new AuditLog(
                    username: caller.Username,
                    repository: policy.RepositoryPattern,
                    actions: $"deleted:{executionResult.DeletedTagsCount}",
                    description: nameof(AuditLogEnums.Description.Retention_Policy_Executed),
                    clientIp: clientAddress?.ToString() ?? string.Empty));

                await context.SaveChangesAsync();

                await webhookDispatcher.DispatchEventAsync(
                    "retention",
                    caller.Username,
                    policy.RepositoryPattern,
                    $"policy '{policy.Name}' deleted {executionResult.DeletedTagsCount} tag(s)");
            }

            return Result<RetentionExecutionResultDto>.Success(executionResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while executing retention policy {Id}.", id);
            return Result<RetentionExecutionResultDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private static string? ValidateDto(
        SaveRetentionPolicyDto dto,
        out string normalizedName,
        out string normalizedPattern,
        out string normalizedProtected)
    {
        normalizedName = dto.Name?.Trim() ?? string.Empty;
        normalizedPattern = dto.RepositoryPattern?.Trim().ToLowerInvariant() ?? string.Empty;
        normalizedProtected = string.Join(
            ",",
            ParseProtectedPatterns(dto.ProtectedTagPatterns));

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return ResponseMessages.InvalidRetentionPolicyRule;
        }

        if (!RepositoryPermissionRepository.IsValidPattern(normalizedPattern))
        {
            return ResponseMessages.InvalidRepositoryPattern;
        }

        var hasKeepCount = dto.KeepLastCount.HasValue;
        var hasMaxAge = dto.MaxAgeDays.HasValue;

        if (!hasKeepCount && !hasMaxAge)
        {
            return ResponseMessages.InvalidRetentionPolicyRule;
        }

        if (hasKeepCount && dto.KeepLastCount!.Value < 1)
        {
            return ResponseMessages.InvalidRetentionPolicyRule;
        }

        if (hasMaxAge && dto.MaxAgeDays!.Value < 1)
        {
            return ResponseMessages.InvalidRetentionPolicyRule;
        }

        if (dto.RunIntervalHours.HasValue && dto.RunIntervalHours.Value < 1)
        {
            return ResponseMessages.InvalidRetentionPolicyRule;
        }

        return null;
    }

    public async Task<int> ExecuteDueScheduledPoliciesAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var scheduledPolicies = await context.RetentionPolicies
            .AsNoTracking()
            .Where(p => p.IsEnabled && p.RunIntervalHours != null && p.RunIntervalHours >= 1)
            .OrderBy(p => p.Id)
            .ToListAsync();

        var duePolicies = scheduledPolicies
            .Where(p => p.LastRunAt == null || p.LastRunAt.Value.AddHours(p.RunIntervalHours!.Value) <= nowUtc)
            .ToList();

        if (duePolicies.Count == 0)
        {
            return 0;
        }

        var adminCaller = await context.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync();

        if (adminCaller is null)
        {
            return 0;
        }

        var executedCount = 0;
        foreach (var policy in duePolicies)
        {
            var res = await ExecutePolicy(policy.Id, dryRun: false, callerId: adminCaller.Id, clientAddress: null);
            if (res.Status == RepositoryStatus.Success)
            {
                executedCount++;
            }
        }

        return executedCount;
    }

    internal static List<string> ParseProtectedPatterns(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string? FindMatchingProtectedPattern(string tag, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, tag, ignoreCase: true))
            {
                return pattern;
            }
        }

        return null;
    }

    private static RetentionPolicyDto MapToDto(RetentionPolicy p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        RepositoryPattern = p.RepositoryPattern,
        KeepLastCount = p.KeepLastCount,
        MaxAgeDays = p.MaxAgeDays,
        ProtectedTagPatterns = p.ProtectedTagPatterns,
        IsEnabled = p.IsEnabled,
        RunIntervalHours = p.RunIntervalHours,
        LastRunAt = p.LastRunAt,
        NextRunAt = p.IsEnabled && p.RunIntervalHours.HasValue
            ? (p.LastRunAt ?? p.CreatedAt).AddHours(p.RunIntervalHours.Value)
            : null,
        LastDeletedTagsCount = p.LastDeletedTagsCount,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    private static readonly Expression<Func<RetentionPolicy, RetentionPolicyDto>> ToDto = p => new RetentionPolicyDto
    {
        Id = p.Id,
        Name = p.Name,
        RepositoryPattern = p.RepositoryPattern,
        KeepLastCount = p.KeepLastCount,
        MaxAgeDays = p.MaxAgeDays,
        ProtectedTagPatterns = p.ProtectedTagPatterns,
        IsEnabled = p.IsEnabled,
        RunIntervalHours = p.RunIntervalHours,
        LastRunAt = p.LastRunAt,
        NextRunAt = p.IsEnabled && p.RunIntervalHours.HasValue
            ? (p.LastRunAt ?? p.CreatedAt).AddHours(p.RunIntervalHours.Value)
            : null,
        LastDeletedTagsCount = p.LastDeletedTagsCount,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
