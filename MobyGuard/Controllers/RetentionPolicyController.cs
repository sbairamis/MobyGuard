using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.RetentionPolicyDtos;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[Authorize(Roles = Roles.Admin)]
public class RetentionPolicyController(RetentionPolicyRepository repository) : CustomBaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(List<RetentionPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<RetentionPolicyDto>>> GetAll()
    {
        var result = await repository.GetAll();
        return HandleResult(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RetentionPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RetentionPolicyDto>> GetById([FromRoute] int id)
    {
        var result = await repository.GetById(id);
        return HandleResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RetentionPolicyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RetentionPolicyDto>> Add([FromBody] SaveRetentionPolicyDto dto)
    {
        var result = await repository.Add(dto);
        return HandleCreatedAtActionResult(result, nameof(GetById), policy => new { id = policy.Id });
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Update([FromRoute] int id, [FromBody] SaveRetentionPolicyDto dto)
    {
        var result = await repository.Update(id, dto);
        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id)
    {
        var result = await repository.DeleteById(id);
        return HandleResult(result);
    }

    [HttpPost("{id}/run")]
    [ProducesResponseType(typeof(RetentionExecutionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RetentionExecutionResultDto>> ExecutePolicy(
        [FromRoute] int id,
        [FromQuery] bool dryRun = false)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.ExecutePolicy(
            id,
            dryRun,
            callerId,
            HttpContext.Connection.RemoteIpAddress);

        return HandleResult(result);
    }
}
