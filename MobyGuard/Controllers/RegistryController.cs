using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.RegistryDtos;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
public class RegistryController(RegistryRepository repository) : CustomBaseController
{
    [HttpGet("repositories")]
    [ProducesResponseType(typeof(List<RegistryRepositoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<RegistryRepositoryDto>>> GetRepositories(
        [FromQuery] string? search = null,
        [FromQuery] bool refresh = false)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.GetRepositories(callerId, search, refresh);

        return HandleResult(result);
    }

    [HttpGet("tags")]
    [ProducesResponseType(typeof(RegistryRepositoryTagsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegistryRepositoryTagsDto>> GetTags(
        [FromQuery] string repositoryName,
        [FromQuery] bool refresh = false)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.GetTags(repositoryName, callerId, refresh);

        return HandleResult(result);
    }

    [HttpGet("manifest")]
    [ProducesResponseType(typeof(RegistryManifestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegistryManifestDto>> GetManifest(
        [FromQuery] string repositoryName,
        [FromQuery] string reference,
        [FromQuery] string? platformDigest = null)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.GetManifest(repositoryName, reference, callerId, platformDigest);

        return HandleResult(result);
    }

    [HttpDelete("manifest")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteManifest(
        [FromQuery] string repositoryName,
        [FromQuery] string reference)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.DeleteManifest(
            repositoryName,
            reference,
            callerId,
            HttpContext.Connection.RemoteIpAddress);

        return HandleResult(result);
    }

    [AllowAnonymous]
    [HttpPost("events")]
    [ProducesResponseType(typeof(RegistryWebhookResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegistryWebhookResultDto>> ReceiveWebhookEvents(
        [FromBody] RegistryEventEnvelopeDto? envelope)
    {
        string? providedSecret = null;

        if (Request.Headers.TryGetValue("X-Registry-Webhook-Secret", out var customSecret)
            && !string.IsNullOrWhiteSpace(customSecret))
        {
            providedSecret = customSecret.ToString().Trim();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var raw = authHeader.ToString().Trim();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedSecret = raw["Bearer ".Length..].Trim();
            }
        }

        var result = await repository.ProcessWebhookEvents(
            envelope,
            providedSecret,
            HttpContext.Connection.RemoteIpAddress);

        return HandleResult(result);
    }
}
