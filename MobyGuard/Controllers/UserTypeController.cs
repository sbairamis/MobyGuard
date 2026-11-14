using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.UserTypeDtos;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[Authorize]
public class UserTypeController(UserTypeRepository repository) : CustomBaseController
{
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(List<UserTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<UserTypeDto>>> GetAll()
    {
        var result = await repository.GetAll();

        return HandleResult(result);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(UserTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserTypeDto>> GetById([FromRoute] int id)
    {
        var result = await repository.GetById(id);

        return HandleResult(result);
    }
}
