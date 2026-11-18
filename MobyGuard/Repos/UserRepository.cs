using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Auth;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.UserDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Global;
using MobyGuard.Helpers;
using MobyGuard.Pagination;

namespace MobyGuard.Repos;

public partial class UserRepository(
    DatabaseContext context,
    IHttpContextAccessor httpContextAccessor,
    IUriService uriService,
    ILogger<UserRepository> logger,
    SessionService sessionService,
    SetupTokenService setupTokenService)
{
    private const int RootAdminUserId = 1;
    private static readonly SemaphoreSlim UserMutationLock = new(1, 1);

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{1,63}$", RegexOptions.Compiled)]
    private static partial Regex UsernameRegex();

    public static bool IsValidUsername(string? username)
        => !string.IsNullOrWhiteSpace(username) && UsernameRegex().IsMatch(username);

    private async Task<bool> IsUsernameTakenAsync(string normalizedUsername, int? excludeUserId = null)
    {
        var normalizedLower = normalizedUsername.ToLowerInvariant();
        return excludeUserId.HasValue
            ? await context.Users.AnyAsync(u => u.Id != excludeUserId.Value && u.Username.ToLower() == normalizedLower)
            : await context.Users.AnyAsync(u => u.Username.ToLower() == normalizedLower);
    }

    public async Task<Result<SetupStatusDto>> GetSetupStatus()
    {
        try
        {
            var hasUsers = await context.Users.AnyAsync();
            if (!hasUsers)
            {
                setupTokenService.EnsureTokenGenerated();
            }
            else
            {
                setupTokenService.InvalidateToken();
            }

            return Result<SetupStatusDto>.Success(new SetupStatusDto
            {
                SetupRequired = !hasUsers,
                ApiVersion = Variables.AppVersion
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while checking setup status");
            return Result<SetupStatusDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> VerifySetupToken(VerifySetupTokenDto dto)
    {
        try
        {
            if (await context.Users.AnyAsync())
            {
                setupTokenService.InvalidateToken();
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.SetupAlreadyCompleted);
            }

            setupTokenService.EnsureTokenGenerated();

            if (!setupTokenService.VerifyToken(dto.SetupToken))
            {
                return Result.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidSetupToken);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while verifying the setup token");
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<LoginResponseDto>> SetupInitialAdmin(SetupAdminDto dto)
    {
        try
        {
            var normalizedUsername = dto.Username?.Trim() ?? string.Empty;

            await UserMutationLock.WaitAsync();
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync();

                if (await context.Users.AnyAsync())
                {
                    setupTokenService.InvalidateToken();
                    return Result<LoginResponseDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.SetupAlreadyCompleted);
                }

                setupTokenService.EnsureTokenGenerated();

                if (!setupTokenService.VerifyToken(dto.SetupToken))
                {
                    return Result<LoginResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidSetupToken);
                }

                if (!IsValidUsername(normalizedUsername))
                {
                    return Result<LoginResponseDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidUsername);
                }

                var (isValidPassword, passwordError) = PasswordPolicyValidator.Validate(dto.Password, normalizedUsername);
                if (!isValidPassword)
                {
                    return Result<LoginResponseDto>.Failure(RepositoryStatus.BadRequest, passwordError);
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
                    Username = normalizedUsername,
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
                await transaction.CommitAsync();

                setupTokenService.InvalidateToken();

                await IssueAuthCookie(user.Id, user.Username, adminType.Type);

                return Result<LoginResponseDto>.Success(new LoginResponseDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    UserType = adminType.Type
                });
            }
            finally
            {
                UserMutationLock.Release();
            }
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
                if (sessionService.IsLockedUnknownUser(loginBodyDto.Username, clientAddress))
                {
                    return Result<LoginResponseDto>.Failure(RepositoryStatus.TooManyRequests, ResponseMessages.TooManyRequests);
                }

                PasswordHasher.VerifyDummy(loginBodyDto.Password);
                sessionService.IncreaseUnknownUserLoginRetries(loginBodyDto.Username, clientAddress);

                return Result<LoginResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            var isLockedUser = sessionService.IsLockedUser(user.Id, clientAddress);

            if (isLockedUser)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.TooManyRequests, ResponseMessages.TooManyRequests);
            }

            var correctPassword = PasswordHasher.Verify(loginBodyDto.Password, user.Salt, user.Password);

            if (!correctPassword)
            {
                sessionService.IncreaseUserLoginRetries(user.Id, clientAddress);

                return Result<LoginResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            if (!user.IsActive)
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.UserIsInactive);
            }

            if (string.Equals(user.UserType?.Type, Roles.Service, StringComparison.OrdinalIgnoreCase))
            {
                return Result<LoginResponseDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.ServiceAccountCannotLogin);
            }

            sessionService.ClearUserLoginRetries(user.Id, clientAddress);

            await IssueAuthCookie(user.Id, user.Username, user.UserType!.Type);

            user.LastLogin = DateTime.UtcNow;

            await context.SaveChangesAsync();

            var loginResponseDto = new LoginResponseDto
            {
                Id = user.Id,
                Username = user.Username,
                UserType = user.UserType!.Type
            };

            return Result<LoginResponseDto>.Success(loginResponseDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to authenticate user: {Username}", loginBodyDto.Username);

            return Result<LoginResponseDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Logout(int callerId, string? jti)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(jti))
            {
                sessionService.RevokeAccessToken(callerId, jti);
            }

            if (httpContextAccessor.HttpContext is not null)
            {
                await httpContextAccessor.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while logging out user with id: {Id}", callerId);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
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
            var normalizedUsername = addUserDto.Username?.Trim() ?? string.Empty;

            if (!IsValidUsername(normalizedUsername))
            {
                return Result<UserDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidUsername);
            }

            await UserMutationLock.WaitAsync();
            try
            {
                if (await IsUsernameTakenAsync(normalizedUsername))
                {
                    return Result<UserDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.UserExists);
                }

                var (isValidPassword, passwordError) = PasswordPolicyValidator.Validate(addUserDto.Password, normalizedUsername);
                if (!isValidPassword)
                {
                    return Result<UserDto>.Failure(RepositoryStatus.BadRequest, passwordError);
                }

                var userType = await context.UserTypes.FirstOrDefaultAsync(ut => ut.Id == addUserDto.UserTypeId);

                if (userType is null)
                {
                    return Result<UserDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserTypeNotFound);
                }

                var salt = PasswordHasher.GenerateSalt();

                var user = new User
                {
                    Username = normalizedUsername,
                    Password = PasswordHasher.Hash(addUserDto.Password, salt),
                    Salt = salt,
                    IsActive = true,
                    UserTypeId = userType.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.Users.Add(user);

                var auditLog = new AuditLog(
                    username: normalizedUsername,
                    description: nameof(AuditLogEnums.Description.User_Created));

                context.AuditLogs.Add(auditLog);

                await context.SaveChangesAsync();

                return Result<UserDto>.Success(new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    IsActive = user.IsActive,
                    LastLogin = user.LastLogin,
                    UserTypeId = user.UserTypeId,
                    UserType = userType.Type,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt
                });
            }
            finally
            {
                UserMutationLock.Release();
            }
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

    public async Task<Result> Update(int id, UpdateUserDto updateUserDto, int callerId)
    {
        try
        {
            var normalizedUsername = updateUserDto.Username?.Trim() ?? string.Empty;

            await UserMutationLock.WaitAsync();
            try
            {
                var user = await context.Users.FindAsync(id);

                if (user is null)
                {
                    return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
                }

                // An admin who demoted or deactivated themselves could leave no active admin behind.
                if (user.Id == callerId && (updateUserDto.UserTypeId != user.UserTypeId || !updateUserDto.IsActive))
                {
                    return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotChangeOwnTypeOrStatus);
                }

                // Protect the initial bootstrap root admin (Id == 1) from being demoted, disabled, or modified by other admins.
                if (user.Id == RootAdminUserId && (callerId != RootAdminUserId || updateUserDto.UserTypeId != user.UserTypeId || !updateUserDto.IsActive))
                {
                    return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotModifyRootAdmin);
                }

                if (!IsValidUsername(normalizedUsername))
                {
                    return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidUsername);
                }

                if (await IsUsernameTakenAsync(normalizedUsername, user.Id))
                {
                    return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.UserExists);
                }

                if (!await context.UserTypes.AnyAsync(userType => userType.Id == updateUserDto.UserTypeId))
                {
                    return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.UserTypeNotFound);
                }

                var shouldInvalidateSessions = !updateUserDto.IsActive
                                               || updateUserDto.UserTypeId != user.UserTypeId
                                               || normalizedUsername != user.Username;

                user.Username = normalizedUsername;
                user.UserTypeId = updateUserDto.UserTypeId;
                user.IsActive = updateUserDto.IsActive;
                user.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                if (shouldInvalidateSessions)
                {
                    sessionService.DeleteUserEntries(id);
                }
                else
                {
                    sessionService.ClearAllUserLoginRetries(id);
                }

                return Result.Success();
            }
            finally
            {
                UserMutationLock.Release();
            }
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
            var user = await context.Users
                .Include(u => u.UserType)
                .FirstOrDefaultAsync(u => u.Id == callerId);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (string.IsNullOrEmpty(changePasswordDto.CurrentPassword)
                || !PasswordHasher.Verify(changePasswordDto.CurrentPassword, user.Salt, user.Password))
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.InvalidCurrentPassword);
            }

            var (isValidPassword, passwordError) = PasswordPolicyValidator.Validate(changePasswordDto.Password, user.Username);
            if (!isValidPassword)
            {
                return Result.Failure(RepositoryStatus.BadRequest, passwordError);
            }

            await SetPassword(user, changePasswordDto.Password, invalidateSessions: true);

            await IssueAuthCookie(user.Id, user.Username, user.UserType!.Type);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to change the password of user with id: {Id}", callerId);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> ResetPassword(int id, ResetPasswordDto resetPasswordDto, int callerId)
    {
        try
        {
            var user = await context.Users.FindAsync(id);

            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            if (user.Id == RootAdminUserId && callerId != RootAdminUserId)
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotModifyRootAdmin);
            }

            var (isValidPassword, passwordError) = PasswordPolicyValidator.Validate(resetPasswordDto.Password, user.Username);
            if (!isValidPassword)
            {
                return Result.Failure(RepositoryStatus.BadRequest, passwordError);
            }

            await SetPassword(user, resetPasswordDto.Password, invalidateSessions: true);

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

            if (user.Id == RootAdminUserId)
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotModifyRootAdmin);
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

            sessionService.DeleteUserEntries(id);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to delete user with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private async Task SetPassword(User user, string newPassword, bool invalidateSessions)
    {
        var salt = PasswordHasher.GenerateSalt();

        user.Salt = salt;
        user.Password = PasswordHasher.Hash(newPassword, salt);
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        if (invalidateSessions)
        {
            sessionService.DeleteUserEntries(user.Id);
        }
        else
        {
            sessionService.ClearAllUserLoginRetries(user.Id);
        }
    }

    private async Task IssueAuthCookie(int userId, string username, string role)
    {
        var jti = Guid.NewGuid().ToString("N");
        var sessionExpiry = TimeSpan.FromDays(Variables.SessionExpiryDays);

        sessionService.StoreActiveAccessToken(userId, jti, sessionExpiry);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("jti", jti),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(sessionExpiry),
            AllowRefresh = true
        };

        if (httpContextAccessor.HttpContext is not null)
        {
            await httpContextAccessor.HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);
        }
    }

    private readonly Expression<Func<User, UserDto>> ToUserDto = user => new UserDto
    {
        Id = user.Id,
        Username = user.Username,
        IsActive = user.IsActive,
        LastLogin = user.LastLogin,
        UserTypeId = user.UserTypeId,
        UserType = user.UserType!.Type,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
