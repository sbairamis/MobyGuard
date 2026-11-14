using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Auth;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.TokenDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Helpers;

namespace MobyGuard.Repos;

public class TokenRepository(
    DatabaseContext context,
    RegistryTokenSigner tokenSigner,
    SessionService sessionService,
    ILogger<TokenRepository> logger)
{
    public async Task<Result<RegistryTokenResponseDto>> IssueToken(
        string? authorizationHeader,
        string? service,
        string[]? scopes,
        IPAddress? clientAddress)
    {
        try
        {
            if (!TryParseBasicAuth(authorizationHeader, out var username, out var password))
            {
                return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            var user = await context.Users
                .Include(u => u.UserType)
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user is null)
            {
                return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            if (sessionService.IsLockedUser(user.Id, clientAddress))
            {
                return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.TooManyRequests, ResponseMessages.TooManyRequests);
            }

            if (!user.IsActive)
            {
                return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.UserIsInactive);
            }

            if (!PasswordHasher.Verify(password, user.Salt, user.Password))
            {
                sessionService.IncreaseUserLoginRetries(user.Id, clientAddress);

                return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.Unauthorized, ResponseMessages.InvalidCredentials);
            }

            sessionService.ClearUserLoginRetries(user.Id, clientAddress);

            var isAdmin = string.Equals(user.UserType?.Type, Roles.Admin, StringComparison.OrdinalIgnoreCase);

            var userPermissions = isAdmin
                ? []
                : await context.RepositoryPermissions
                    .AsNoTracking()
                    .Where(p => p.UserId == user.Id)
                    .ToListAsync();

            var accessEntries = EvaluateScopes(scopes, isAdmin, userPermissions);

            var (jwt, expiresInSeconds, issuedAtUtc) = tokenSigner.CreateToken(user.Username, service, accessEntries);

            user.LastLogin = issuedAtUtc;

            var loggedRepo = accessEntries.Count > 0
                ? string.Join(";", accessEntries.Select(a => $"{a.Type}:{a.Name}"))
                : "login";

            var loggedActions = accessEntries.Count > 0
                ? string.Join(";", accessEntries.Select(a => string.Join(",", a.Actions)))
                : "auth";

            context.AuditLogs.Add(new AuditLog(
                username: user.Username,
                repository: loggedRepo,
                actions: loggedActions,
                description: nameof(AuditLogEnums.Description.Token_Issued),
                clientIp: clientAddress?.ToString() ?? string.Empty));

            await context.SaveChangesAsync();

            return Result<RegistryTokenResponseDto>.Success(new RegistryTokenResponseDto
            {
                Token = jwt,
                AccessToken = jwt,
                ExpiresIn = expiresInSeconds,
                IssuedAt = issuedAtUtc
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while issuing a Docker registry token.");

            return Result<RegistryTokenResponseDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private static List<RegistryAccessEntryDto> EvaluateScopes(
        string[]? rawScopes,
        bool isAdmin,
        List<RepositoryPermission> userPermissions)
    {
        var entries = new List<RegistryAccessEntryDto>();

        if (rawScopes is null || rawScopes.Length == 0)
        {
            return entries;
        }

        // Scopes may arrive as multiple query params (?scope=a&scope=b) or space-delimited (?scope=a%20b).
        var individualScopes = rawScopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var scopeStr in individualScopes)
        {
            if (!TryParseScope(scopeStr, out var resourceType, out var resourceName, out var requestedActions))
            {
                continue;
            }

            var grantedActions = ResolveGrantedActions(resourceType, resourceName, requestedActions, isAdmin, userPermissions);

            entries.Add(new RegistryAccessEntryDto
            {
                Type = resourceType,
                Name = resourceName,
                Actions = grantedActions
            });
        }

        return entries;
    }

    private static List<string> ResolveGrantedActions(
        string resourceType,
        string resourceName,
        string[] requestedActions,
        bool isAdmin,
        List<RepositoryPermission> userPermissions)
    {
        if (isAdmin)
        {
            return requestedActions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.Equals(resourceType, "registry", StringComparison.OrdinalIgnoreCase)
            && string.Equals(resourceName, "catalog", StringComparison.OrdinalIgnoreCase))
        {
            // Non-admin users only get catalog access if they have a global "*" pull permission.
            var hasGlobalPull = userPermissions.Any(p => p.RepositoryPattern == "*" && p.CanPull);
            return hasGlobalPull ? requestedActions.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [];
        }

        if (!string.Equals(resourceType, "repository", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        bool allowPull = false;
        bool allowPush = false;
        bool allowDelete = false;

        foreach (var permission in userPermissions)
        {
            if (RepositoryPermissionRepository.PatternMatchesRepository(permission.RepositoryPattern, resourceName))
            {
                allowPull |= permission.CanPull;
                allowPush |= permission.CanPush;
                allowDelete |= permission.CanDelete;
            }
        }

        var granted = new List<string>(requestedActions.Length);

        foreach (var action in requestedActions)
        {
            var normalized = action.ToLowerInvariant();
            var isAllowed = normalized switch
            {
                "pull" => allowPull,
                "push" => allowPush,
                "delete" => allowDelete,
                "*" => allowPull && allowPush && allowDelete,
                _ => false
            };

            if (isAllowed && !granted.Contains(normalized))
            {
                granted.Add(normalized);
            }
        }

        return granted;
    }

    // Format: <type>:<name>:<action1,action2>
    private static bool TryParseScope(
        string scope,
        out string resourceType,
        out string resourceName,
        out string[] actions)
    {
        resourceType = string.Empty;
        resourceName = string.Empty;
        actions = [];

        var firstColon = scope.IndexOf(':');
        var lastColon = scope.LastIndexOf(':');

        if (firstColon <= 0 || lastColon <= firstColon || lastColon == scope.Length - 1)
        {
            return false;
        }

        resourceType = scope[..firstColon].Trim();
        resourceName = scope[(firstColon + 1)..lastColon].Trim();
        var actionsPart = scope[(lastColon + 1)..].Trim();

        actions = actionsPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return !string.IsNullOrEmpty(resourceType) && !string.IsNullOrEmpty(resourceName);
    }

    private static bool TryParseBasicAuth(string? authHeader, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (string.IsNullOrWhiteSpace(authHeader)
            || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encodedCredentials = authHeader["Basic ".Length..].Trim();

        try
        {
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var decoded = Encoding.UTF8.GetString(decodedBytes);
            var separatorIndex = decoded.IndexOf(':');

            if (separatorIndex <= 0)
            {
                return false;
            }

            username = decoded[..separatorIndex];
            password = decoded[(separatorIndex + 1)..];

            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
