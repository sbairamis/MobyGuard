using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Classes;
using MobyGuard.Enumerators;

namespace MobyGuard.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class CustomBaseController : ControllerBase
{
    private int? CallerId => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    protected bool TryGetCallerId(out int callerId)
    {
        if (CallerId.HasValue)
        {
            callerId = CallerId.Value;

            return true;
        }

        callerId = 0;

        return false;
    }

    protected ActionResult HandleResult(Result result)
    {
        return result.Status == RepositoryStatus.Success ? NoContent() : HandleFailure(result);
    }

    protected ActionResult HandleResult<T>(Result<T> result)
    {
        return result.Status switch
        {
            RepositoryStatus.Success => Ok(result.Data),
            RepositoryStatus.Created => StatusCode(StatusCodes.Status201Created, result.Data),
            _ => HandleFailure(result)
        };
    }

    protected ActionResult HandleCreatedResult<T>(Result<T> result, string uri = "")
    {
        return result.Status == RepositoryStatus.Success ? Created(uri, result.Data) : HandleFailure(result);
    }

    protected ActionResult HandleCreatedAtActionResult<T>(Result<T> result, string actionName, Func<T, object> routeValues)
    {
        if (result.Status == RepositoryStatus.Success && result.Data is not null)
        {
            return CreatedAtAction(actionName, routeValues(result.Data), result.Data);
        }

        return HandleResult(result);
    }

    private ObjectResult HandleFailure(Result result)
    {
        var statusCode = result.Status switch
        {
            RepositoryStatus.BadRequest => StatusCodes.Status400BadRequest,
            RepositoryStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            RepositoryStatus.Forbidden => StatusCodes.Status403Forbidden,
            RepositoryStatus.NotFound => StatusCodes.Status404NotFound,
            RepositoryStatus.Conflict => StatusCodes.Status409Conflict,
            RepositoryStatus.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(detail: result.ResponseMessage, statusCode: statusCode);
    }
}
