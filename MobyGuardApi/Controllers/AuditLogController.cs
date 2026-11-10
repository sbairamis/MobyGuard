using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuardApi.Auth;
using MobyGuardApi.Database.Dtos.AuditLogDtos;
using MobyGuardApi.Database.Models;
using MobyGuardApi.Pagination;
using MobyGuardApi.Repos;

namespace MobyGuardApi.Controllers;

[Authorize]
public class AuditLogController(AuditLogRepository repository) : CustomBaseController
{
    [HttpGet("filters")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(AuditLogSearchFiltersDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AuditLogSearchFiltersDto>> GetSearchFilters()
    {
        var result = await repository.GetSearchFilters();

        return HandleResult(result);
    }

    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(PagedResponse<List<AuditLog>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedResponse<List<AuditLog>>>> Search(
        [FromQuery] LogsSearchRequestDto logsSearchRequestDto,
        [FromQuery] PaginationFilter filter)
    {
        var route = Request.Path.Value ?? string.Empty;

        var result = await repository.Search(logsSearchRequestDto, filter, route);

        return HandleResult(result);
    }
}
