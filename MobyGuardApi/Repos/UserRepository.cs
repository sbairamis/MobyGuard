using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MobyGuardApi.Auth;
using MobyGuardApi.Classes;
using MobyGuardApi.Configuration;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Dtos.UserDtos;
using MobyGuardApi.Database.Models;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Helpers;
using MobyGuardApi.Pagination;

namespace MobyGuardApi.Repos;

public class UserRepository(
    DatabaseContext context,
    IOptions<JwtOptions> jwtOptions,
    IUriService uriService,
    ILogger<UserRepository> logger,
    RedisHelper redisHelper)
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<Result<SetupStatusDto>> GetSetupStatus()
    {
        try
        {
            var hasUsers = await context.Users.AnyAsync();
            return Result<SetupStatusDto>.Success(new SetupStatusDto { SetupRequired = !hasUsers });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while checking setup status");
            return Result<SetupStatusDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<LoginResponseDto>> SetupInitialAdmin(SetupAdminDto dto)
    {
        try
        {
            if (await context.Users.AnyAsync())
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.SetupAlreadyCompleted);
            }

            var adminType = await context.UserTypes.FirstOrDefaultAsync(ut => ut.Type == Roles.Admin);
            if (adminType is null)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserTypeNotFound);
            }

            var salt = PasswordHasher.GenerateSalt();
            var now = DateTime.UtcNow;

            var user = new User
            {
                Username = dto.Username.Trim(),
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                Email = dto.Email.Trim(),
                Password = PasswordHasher.Hash(dto.Password, salt),
                Salt = salt,
                IsActive = true,
                UserTypeId = adminType.Id,
                LastLogin = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.Users.Add(user);
            context.AuditLogs.Add(new AuditLog(
                username: user.Username,
                description: nameof(AuditLogEnums.Description.User_Created)));

            await context.SaveChangesAsync();

            var token = GenerateJwtToken(user.Id, user.Username, adminType.Type);

            return Result<LoginResponseDto>.Success(new LoginResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                UserType = adminType.Type,
                Token = token
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while setting up the initial admin user");
            return Result<LoginResponseDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<LoginResponseDto>> Login(LoginUserDto loginBodyDto, IPAddress? clientAddress)
    {
        try
        {
            var user = await context.Users
                .Include(u => u.UserType)
                .FirstOrDefaultAsync(u => u.Username == loginBodyDto.Username);

            if (user is null)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            var isLockedUser = await redisHelper.IsLockedUser(user.Id, clientAddress);

            if (isLockedUser)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.TooManyRequests, ResponseMessages.TooManyRequests);
            }

            if (!user.IsActive)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.UserIsInactive);
            }

            if (string.Equals(user.UserType?.Type, Roles.Service, StringComparison.OrdinalIgnoreCase))
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.ServiceAccountCannotLogin);
            }

            var correctPassword = PasswordHasher.Verify(loginBodyDto.Password, user.Salt, user.Password);

            if (!correctPassword)
            {
                await redisHelper.IncreaseUserLoginRetries(user.Id, clientAddress);

                return Result<LoginResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            await redisHelper.ClearUserLoginRetries(user.Id, clientAddress);

            var accessToken = GenerateJwtToken(user.Id, user.Username, user.UserType!.Type);

            user.LastLogin = DateTime.UtcNow;

            await context.SaveChangesAsync();

            var loginResponseDto = new LoginResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                UserType = user.UserType!.Type,
                Token = accessToken,
            };

            return Result<LoginResponseDto>.Success(loginResponseDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to authenticate user: {Username}", loginBodyDto.Username);

            return Result<LoginResponseDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<PagedResponse<List<UserDto>>>> GetAllWithTypePaged(UserQueryFilterDto filter, string route)
    {
        try
        {
            var validFilter = new PaginationFilter(filter.PageNumber, filter.PageSize);

            var query = context.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter.Username))
            {
                var normalizedUsername = filter.Username.Trim().ToLowerInvariant();

                query = query.Where(user => user.Username.ToLower().Contains(normalizedUsername));
            }

            if (filter.UserTypeId.HasValue)
            {
                query = query.Where(user => user.UserTypeId == filter.UserTypeId.Value);
            }

            if (filter.IsActive.HasValue)
            {
                query = query.Where(user => user.IsActive == filter.IsActive.Value);
            }

            if (filter.FromDate.HasValue)
            {
                query = query.Where(user => user.CreatedAt >= filter.FromDate.Value);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(user => user.CreatedAt <= filter.ToDate.Value);
            }

            var totalRecords = await query.CountAsync();

            var results = await query
                .OrderBy(user => user.Id)
                .Skip((validFilter.PageNumber - 1) * validFilter.PageSize)
                .Take(validFilter.PageSize)
                .Select(ToUserDto)
                .ToListAsync();

            var pagedResponse = PaginationHelper.CreatePagedResponse(results, validFilter, totalRecords, uriService, route);

            return Result<PagedResponse<List<UserDto>>>.Success(pagedResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to fetch page {PageNumber} of users", filter.PageNumber);

            return Result<PagedResponse<List<UserDto>>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<UserDto>> Add(AddUserDto addUserDto)
    {
        try
        {
            if (await context.Users.AnyAsync(user => user.Username == addUserDto.Username))
            {
                return Result<UserDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.UserExists);
            }

            if (await context.Users.AnyAsync(user => user.Email == addUserDto.Email))
            {
                return Result<UserDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.EmailExists);
            }

            var userType = await context.UserTypes.FirstOrDefaultAsync(ut => ut.Id == addUserDto.UserTypeId);

            if (userType is null)
            {
                return Result<UserDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserTypeNotFound);
            }

            var salt = PasswordHasher.GenerateSalt();

            var user = new User
            {
                Username = addUserDto.Username,
                FirstName = addUserDto.FirstName,
                LastName = addUserDto.LastName,
                Email = addUserDto.Email,
                Password = PasswordHasher.Hash(addUserDto.Password, salt),
                Salt = salt,
                IsActive = true,
                UserTypeId = userType.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Users.Add(user);

            var auditLog = new AuditLog(
                username: addUserDto.Username,
                description: nameof(AuditLogEnums.Description.User_Created));

            context.AuditLogs.Add(auditLog);

            await context.SaveChangesAsync();

            return Result<UserDto>.Success(new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                IsActive = user.IsActive,
                LastLogin = user.LastLogin,
                UserTypeId = user.UserTypeId,
                UserType = userType.Type,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to add user with username: {Username}", addUserDto.Username);

            return Result<UserDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<UserDto>> GetById(int id)
    {
        try
        {
            var user = await context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(ToUserDto)
                .FirstOrDefaultAsync();

            if (user is null)
            {
                return Result<UserDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            return Result<UserDto>.Success(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to fetch user with id: {Id}", id);

            return Result<UserDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> UpdateMe(UpdateMeDto updateMeDto, int callerId)
    {
        try
        {
            var user = await context.Users.FindAsync(callerId);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (updateMeDto.Email != user.Email
                && await context.Users.AnyAsync(other => other.Id != user.Id && other.Email == updateMeDto.Email))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.EmailExists);
            }

            user.FirstName = updateMeDto.FirstName;
            user.LastName = updateMeDto.LastName;
            user.Email = updateMeDto.Email;
            user.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            await redisHelper.DeleteUserEntries(callerId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to update profile for user with id: {Id}", callerId);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Update(int id, UpdateUserDto updateUserDto, int callerId)
    {
        try
        {
            var user = await context.Users.FindAsync(id);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (updateUserDto.Email != user.Email
                && await context.Users.AnyAsync(other => other.Id != user.Id && other.Email == updateUserDto.Email))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.EmailExists);
            }

            // An admin who demoted or deactivated themselves could leave no active admin behind.
            if (user.Id == callerId && (updateUserDto.UserTypeId != user.UserTypeId || !updateUserDto.IsActive))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotChangeOwnTypeOrStatus);
            }

            if (updateUserDto.Username != user.Username
                && await context.Users.AnyAsync(other => other.Id != user.Id && other.Username == updateUserDto.Username))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.UserExists);
            }

            if (!await context.UserTypes.AnyAsync(userType => userType.Id == updateUserDto.UserTypeId))
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserTypeNotFound);
            }

            user.Username = updateUserDto.Username;
            user.UserTypeId = updateUserDto.UserTypeId;
            user.IsActive = updateUserDto.IsActive;
            user.FirstName = updateUserDto.FirstName;
            user.LastName = updateUserDto.LastName;
            user.Email = updateUserDto.Email;
            user.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            await redisHelper.DeleteUserEntries(id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to update user with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> ChangePassword(ChangePasswordDto changePasswordDto, int callerId)
    {
        try
        {
            var user = await context.Users.FindAsync(callerId);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (string.IsNullOrEmpty(changePasswordDto.CurrentPassword)
                || !PasswordHasher.Verify(changePasswordDto.CurrentPassword, user.Salt, user.Password))
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidCurrentPassword);
            }

            await SetPassword(user, changePasswordDto.Password);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to change the password of user with id: {Id}", callerId);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> ResetPassword(int id, ResetPasswordDto resetPasswordDto)
    {
        try
        {
            var user = await context.Users.FindAsync(id);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            await SetPassword(user, resetPasswordDto.Password);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to reset the password of user with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteById(int id, int callerId)
    {
        try
        {
            var user = await context.Users.FindAsync(id);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (user.Id == callerId)
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotDeleteYourself);
            }

            var permissions = await context.RepositoryPermissions
                .Where(p => p.UserId == id)
                .ToListAsync();

            if (permissions.Count > 0)
            {
                context.RepositoryPermissions.RemoveRange(permissions);
            }

            context.Users.Remove(user);

            await context.SaveChangesAsync();

            await redisHelper.DeleteUserEntries(id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to delete user with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private async Task SetPassword(User user, string newPassword)
    {
        var salt = PasswordHasher.GenerateSalt();

        user.Salt = salt;
        user.Password = PasswordHasher.Hash(newPassword, salt);
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        await redisHelper.DeleteUserEntries(user.Id);
    }

    private string GenerateJwtToken(int userId, string username, string role)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtOptions.SecurityKey));

        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_jwtOptions.TokenExpiryDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private readonly Expression<Func<User, UserDto>> ToUserDto = user => new UserDto
    {
        Id = user.Id,
        Username = user.Username,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        IsActive = user.IsActive,
        LastLogin = user.LastLogin,
        UserTypeId = user.UserTypeId,
        UserType = user.UserType!.Type,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
