using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuardApi.Auth;
using MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;
using MobyGuardApi.Pagination;
using MobyGuardApi.Repos;

namespace MobyGuardApi.Controllers;

[Authorize]
public class RepositoryPermissionController(RepositoryPermissionRepository repository) : CustomBaseController
{
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(PagedResponse<List<RepositoryPermissionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedResponse<List<RepositoryPermissionDto>>>> GetAllPaged(
        [FromQuery] RepositoryPermissionQueryFilterDto filter)
    {
        var route = Request.Path.Value ?? string.Empty;

        var result = await repository.GetAllPaged(filter, route);

        return HandleResult(result);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(RepositoryPermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RepositoryPermissionDto>> GetById([FromRoute] uint id)
    {
        var result = await repository.GetById(id);

        return HandleResult(result);
    }

    [HttpGet("user/{userId}")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(List<RepositoryPermissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<RepositoryPermissionDto>>> GetByUserId([FromRoute] int userId)
    {
        var result = await repository.GetByUserId(userId);

        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(RepositoryPermissionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RepositoryPermissionDto>> Add([FromBody] AddRepositoryPermissionDto dto)
    {
        var result = await repository.Add(dto);

        return HandleCreatedAtActionResult(result, nameof(GetById), permission => new { id = permission.Id });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Update([FromRoute] uint id, [FromBody] UpdateRepositoryPermissionDto dto)
    {
        var result = await repository.Update(id, dto);

        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] uint id)
    {
        var result = await repository.DeleteById(id);

        return HandleResult(result);
    }
}
