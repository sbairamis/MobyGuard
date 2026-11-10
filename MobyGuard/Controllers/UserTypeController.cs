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

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(UserTypeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserTypeDto>> Add([FromBody] CreateUserTypeDto createUserTypeDto)
    {
        var result = await repository.Add(createUserTypeDto);

        return HandleCreatedAtActionResult(result, nameof(GetById), ut => new { id = ut.Id });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Update([FromRoute] int id, [FromBody] UpdateUserTypeDto updateUserTypeDto)
    {
        var result = await repository.Update(id, updateUserTypeDto);

        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id)
    {
        var result = await repository.DeleteById(id);

        return HandleResult(result);
    }
}
