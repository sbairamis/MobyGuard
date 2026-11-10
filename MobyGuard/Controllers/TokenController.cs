using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.TokenDtos;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[AllowAnonymous]
public class TokenController(TokenRepository repository, RegistryTokenSigner tokenSigner) : CustomBaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(RegistryTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegistryTokenResponseDto>> GetToken(
        [FromQuery(Name = "service")] string? service = null,
        [FromQuery(Name = "scope")] string[]? scope = null)
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();

        var result = await repository.IssueToken(
            authHeader,
            service,
            scope,
            HttpContext.Connection.RemoteIpAddress);

        return HandleResult(result);
    }

    [HttpGet("jwks")]
    [ProducesResponseType(typeof(RegistryJwksResponseDto), StatusCodes.Status200OK)]
    public ActionResult<RegistryJwksResponseDto> GetJwks()
    {
        return Ok(tokenSigner.GetJwks());
    }
}
