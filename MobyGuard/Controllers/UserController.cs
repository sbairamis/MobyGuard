using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobyGuard.Auth;
using MobyGuard.Database.Dtos.UserDtos;
using MobyGuard.Pagination;
using MobyGuard.Repos;

namespace MobyGuard.Controllers;

[Authorize]
public class UserController(UserRepository repository) : CustomBaseController
{
    [HttpGet("setup-status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SetupStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SetupStatusDto>> GetSetupStatus()
    {
        var result = await repository.GetSetupStatus();

        return HandleResult(result);
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginResponseDto>> SetupInitialAdmin(SetupAdminDto setupAdminDto)
    {
        var result = await repository.SetupInitialAdmin(setupAdminDto);

        return HandleResult(result);
    }

    [HttpGet("me")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetMe()
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.GetById(callerId);

        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginUserDto loginUserDto)
    {
        var result = await repository.Login(loginUserDto, HttpContext.Connection.RemoteIpAddress);

        return HandleResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginResponseDto>> Refresh(RefreshTokenDto refreshTokenDto)
    {
        var result = await repository.Refresh(refreshTokenDto);

        return HandleResult(result);
    }

    [HttpPost("logout")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Logout([FromBody] RefreshTokenDto? refreshTokenDto)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        var result = await repository.Logout(refreshTokenDto, callerId, jti);

        return HandleResult(result);
    }

    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(PagedResponse<List<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedResponse<List<UserDto>>>> GetAllWithTypePaged([FromQuery] UserQueryFilterDto filter)
    {
        var route = Request.Path.Value ?? string.Empty;

        var result = await repository.GetAllWithTypePaged(filter, route);

        return HandleResult(result);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserDto>> GetById([FromRoute] int id)
    {
        var result = await repository.GetById(id);

        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UserDto>> Add(AddUserDto addUserDto)
    {
        var result = await repository.Add(addUserDto);

        return HandleCreatedAtActionResult(result, nameof(GetById), user => new { id = user.Id });
    }

    [HttpPut("me")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> UpdateMe(UpdateMeDto updateMeDto)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.UpdateMe(updateMeDto, callerId);

        return HandleResult(result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Update([FromRoute] int id, [FromBody] UpdateUserDto updateUserDto)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.Update(id, updateUserDto, callerId);

        return HandleResult(result);
    }

    [HttpPut("me/password")]
    [Authorize(Roles = $"{Roles.Admin}, {Roles.User}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ChangePassword(ChangePasswordDto changePasswordDto)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.ChangePassword(changePasswordDto, callerId);

        return HandleResult(result);
    }

    [HttpPut("{id}/password")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ResetPassword([FromRoute] int id, [FromBody] ResetPasswordDto resetPasswordDto)
    {
        var result = await repository.ResetPassword(id, resetPasswordDto);

        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteById([FromRoute] int id)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var result = await repository.DeleteById(id, callerId);

        return HandleResult(result);
    }
}
