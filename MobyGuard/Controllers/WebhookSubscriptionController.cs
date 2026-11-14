using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.WebhookSubscriptionDtos;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[Authorize(Roles = Roles.Admin)]
public class WebhookSubscriptionController(WebhookSubscriptionRepository repository) : CustomBaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(List<WebhookSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<WebhookSubscriptionDto>>> GetAll()
    {
        var result = await repository.GetAll();
        return HandleResult(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WebhookSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WebhookSubscriptionDto>> GetById([FromRoute] int id)
    {
        var result = await repository.GetById(id);
        return HandleResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WebhookSubscriptionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WebhookSubscriptionDto>> Add([FromBody] SaveWebhookSubscriptionDto dto)
    {
        var result = await repository.Add(dto);
        return HandleCreatedAtActionResult(result, nameof(GetById), item => new { id = item.Id });
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Update([FromRoute] int id, [FromBody] SaveWebhookSubscriptionDto dto)
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

    [HttpPost("{id}/test")]
    [ProducesResponseType(typeof(WebhookDeliveryResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WebhookDeliveryResultDto>> TestSubscription([FromRoute] int id)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.TestSubscription(id, callerId);
        return HandleResult(result);
    }
}
