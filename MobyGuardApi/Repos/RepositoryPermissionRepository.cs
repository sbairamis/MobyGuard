using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;
using MobyGuardApi.Database.Models;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Pagination;

namespace MobyGuardApi.Repos;

public partial class RepositoryPermissionRepository(
    DatabaseContext context,
    IUriService uriService,
    ILogger<RepositoryPermissionRepository> logger)
{
    // Matches "*", "<namespace>/*", or valid Docker repository names ("team-a/app", "library/nginx", "my-app").
    [GeneratedRegex(@"^(\*|[a-z0-9]+([._-][a-z0-9]+)*(\/[a-z0-9]+([._-][a-z0-9]+)*)*(\/\*)?)$", RegexOptions.Compiled)]
    private static partial Regex RepositoryPatternRegex();

    public async Task<Result<PagedResponse<List<RepositoryPermissionDto>>>> GetAllPaged(
        RepositoryPermissionQueryFilterDto filter,
        string route)
    {
        try
        {
            var validFilter = new PaginationFilter(filter.PageNumber, filter.PageSize);

            var query = context.RepositoryPermissions.AsNoTracking();

            if (filter.UserId.HasValue)
            {
                query = query.Where(p => p.UserId == filter.UserId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Username))
            {
                var normalizedUsername = filter.Username.Trim().ToLowerInvariant();
                query = query.Where(p => p.User!.Username.ToLower().Contains(normalizedUsername));
            }

            if (!string.IsNullOrWhiteSpace(filter.RepositoryPattern))
            {
                var normalizedPattern = filter.RepositoryPattern.Trim().ToLowerInvariant();
                query = query.Where(p => p.RepositoryPattern.ToLower().Contains(normalizedPattern));
            }

            var totalRecords = await query.CountAsync();

            var results = await query
                .OrderBy(p => p.Id)
                .Skip((validFilter.PageNumber - 1) * validFilter.PageSize)
                .Take(validFilter.PageSize)
                .Select(ToDto)
                .ToListAsync();

            var pagedResponse = PaginationHelper.CreatePagedResponse(results, validFilter, totalRecords, uriService, route);

            return Result<PagedResponse<List<RepositoryPermissionDto>>>.Success(pagedResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching page {PageNumber} of repository permissions.", filter.PageNumber);

            return Result<PagedResponse<List<RepositoryPermissionDto>>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RepositoryPermissionDto>> GetById(uint id)
    {
        try
        {
            var permission = await context.RepositoryPermissions
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(ToDto)
                .FirstOrDefaultAsync();

            if (permission is null)
            {
                return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.RepositoryPermissionNotFound);
            }

            return Result<RepositoryPermissionDto>.Success(permission);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching repository permission with id: {Id}", id);

            return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<List<RepositoryPermissionDto>>> GetByUserId(int userId)
    {
        try
        {
            if (!await context.Users.AnyAsync(u => u.Id == userId))
            {
                return Result<List<RepositoryPermissionDto>>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            var permissions = await context.RepositoryPermissions
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.RepositoryPattern)
                .Select(ToDto)
                .ToListAsync();

            return Result<List<RepositoryPermissionDto>>.Success(permissions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching repository permissions for user {UserId}", userId);

            return Result<List<RepositoryPermissionDto>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RepositoryPermissionDto>> Add(AddRepositoryPermissionDto dto)
    {
        try
        {
            var pattern = dto.RepositoryPattern.Trim().ToLowerInvariant();

            if (!IsValidPattern(pattern))
            {
                return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidRepositoryPattern);
            }

            if (!dto.CanPull && !dto.CanPush && !dto.CanDelete)
            {
                return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.NoPermissionActionsSelected);
            }

            var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dto.UserId);

            if (user is null)
            {
                return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserNotFound);
            }

            if (await context.RepositoryPermissions.AnyAsync(p => p.UserId == dto.UserId && p.RepositoryPattern == pattern))
            {
                return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.RepositoryPermissionExists);
            }

            var entity = new RepositoryPermission
            {
                UserId = dto.UserId,
                RepositoryPattern = pattern,
                CanPull = dto.CanPull,
                CanPush = dto.CanPush,
                CanDelete = dto.CanDelete,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.RepositoryPermissions.Add(entity);

            context.AuditLogs.Add(new AuditLog(
                username: user.Username,
                repository: pattern,
                actions: FormatActions(dto.CanPull, dto.CanPush, dto.CanDelete),
                description: nameof(AuditLogEnums.Description.Permission_Created)));

            await context.SaveChangesAsync();

            return Result<RepositoryPermissionDto>.Success(new RepositoryPermissionDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                Username = user.Username,
                RepositoryPattern = entity.RepositoryPattern,
                CanPull = entity.CanPull,
                CanPush = entity.CanPush,
                CanDelete = entity.CanDelete,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while adding repository permission for user {UserId}", dto.UserId);

            return Result<RepositoryPermissionDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Update(uint id, UpdateRepositoryPermissionDto dto)
    {
        try
        {
            var entity = await context.RepositoryPermissions
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RepositoryPermissionNotFound);
            }

            var pattern = dto.RepositoryPattern.Trim().ToLowerInvariant();

            if (!IsValidPattern(pattern))
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidRepositoryPattern);
            }

            if (!dto.CanPull && !dto.CanPush && !dto.CanDelete)
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.NoPermissionActionsSelected);
            }

            if (!string.Equals(entity.RepositoryPattern, pattern, StringComparison.OrdinalIgnoreCase)
                && await context.RepositoryPermissions.AnyAsync(p => p.Id != id && p.UserId == entity.UserId && p.RepositoryPattern == pattern))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.RepositoryPermissionExists);
            }

            entity.RepositoryPattern = pattern;
            entity.CanPull = dto.CanPull;
            entity.CanPush = dto.CanPush;
            entity.CanDelete = dto.CanDelete;
            entity.UpdatedAt = DateTime.UtcNow;

            context.AuditLogs.Add(new AuditLog(
                username: entity.User?.Username ?? string.Empty,
                repository: pattern,
                actions: FormatActions(dto.CanPull, dto.CanPush, dto.CanDelete),
                description: nameof(AuditLogEnums.Description.Permission_Updated)));

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating repository permission with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteById(uint id)
    {
        try
        {
            var entity = await context.RepositoryPermissions
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (entity is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RepositoryPermissionNotFound);
            }

            context.RepositoryPermissions.Remove(entity);

            context.AuditLogs.Add(new AuditLog(
                username: entity.User?.Username ?? string.Empty,
                repository: entity.RepositoryPattern,
                actions: FormatActions(entity.CanPull, entity.CanPush, entity.CanDelete),
                description: nameof(AuditLogEnums.Description.Permission_Deleted)));

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting repository permission with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public static bool IsValidPattern(string pattern)
        => !string.IsNullOrWhiteSpace(pattern) && RepositoryPatternRegex().IsMatch(pattern);

    public static bool PatternMatchesRepository(string pattern, string repositoryName)
    {
        if (pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1]; // Keep trailing '/' (e.g. "olympia/")
            return repositoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, repositoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatActions(bool canPull, bool canPush, bool canDelete)
    {
        var list = new List<string>(3);
        if (canPull) list.Add("pull");
        if (canPush) list.Add("push");
        if (canDelete) list.Add("delete");
        return string.Join(",", list);
    }

    private readonly Expression<Func<RepositoryPermission, RepositoryPermissionDto>> ToDto = p => new RepositoryPermissionDto
    {
        Id = p.Id,
        UserId = p.UserId,
        Username = p.User!.Username,
        RepositoryPattern = p.RepositoryPattern,
        CanPull = p.CanPull,
        CanPush = p.CanPush,
        CanDelete = p.CanDelete,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
