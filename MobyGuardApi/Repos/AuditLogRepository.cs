using Microsoft.EntityFrameworkCore;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Dtos.AuditLogDtos;
using MobyGuardApi.Database.Models;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Pagination;

using static MobyGuardApi.Enumerators.AuditLogEnums;
using static MobyGuardApi.Helpers.EnumsHelper;

namespace MobyGuardApi.Repos;

public class AuditLogRepository(DatabaseContext context, IUriService uriService, ILogger<AuditLogRepository> logger)
{
    public Task<Result<AuditLogSearchFiltersDto>> GetSearchFilters()
    {
        try
        {
            var filters = new AuditLogSearchFiltersDto
            {
                SearchBy = EnumToDictionary<SearchBy>(),
                ValuesStr = new AuditLogStringFiltersDto
                {
                    Description = EnumToDictionary<Description>()
                }
            };

            return Task.FromResult(Result<AuditLogSearchFiltersDto>.Success(filters));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to fetch audit log search filters");

            return Task.FromResult(Result<AuditLogSearchFiltersDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError));
        }
    }

    public async Task<Result<PagedResponse<List<AuditLog>>>> Search(LogsSearchRequestDto logsSearchRequestDto, PaginationFilter filter, string route)
    {
        try
        {
            var validFilter = new PaginationFilter(filter.PageNumber, filter.PageSize);

            var baseQuery = GenerateWhereClause(logsSearchRequestDto);

            var totalRecords = await baseQuery.CountAsync();

            var results = await baseQuery
                .OrderByDescending(auditLog => auditLog.Id)
                .Skip((validFilter.PageNumber - 1) * validFilter.PageSize)
                .Take(validFilter.PageSize)
                .ToListAsync();

            var pagedResponse = PaginationHelper.CreatePagedResponse(results, validFilter, totalRecords, uriService, route);

            return Result<PagedResponse<List<AuditLog>>>.Success(pagedResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to search audit logs on page {PageNumber}", filter.PageNumber);

            return Result<PagedResponse<List<AuditLog>>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private IQueryable<AuditLog> GenerateWhereClause(LogsSearchRequestDto logsSearchRequestDto)
    {
        var value = (logsSearchRequestDto.ValueStr ?? string.Empty).Trim().ToLowerInvariant();

        var baseQuery = context.AuditLogs.AsNoTracking();

        return logsSearchRequestDto.SearchBy switch
        {
            SearchBy.Username    => baseQuery.Where(a => a.Username.ToLower().Contains(value)),
            SearchBy.Repository  => baseQuery.Where(a => a.Repository.ToLower().Contains(value)),
            SearchBy.Actions     => baseQuery.Where(a => a.Actions.ToLower().Contains(value)),
            SearchBy.Description => baseQuery.Where(a => a.Description.ToLower().Contains(value)),
            SearchBy.ClientIp    => baseQuery.Where(a => a.ClientIp.ToLower().Contains(value)),
            SearchBy.Timestamp   => baseQuery.Where(a => a.CreatedAt >= logsSearchRequestDto.FromDate && a.CreatedAt <= logsSearchRequestDto.ToDate),
            _                    => baseQuery
        };
    }
}
