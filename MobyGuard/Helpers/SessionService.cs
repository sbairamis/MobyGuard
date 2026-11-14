using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MobyGuard.Database.Context;
using MobyGuard.Database.Models;

namespace MobyGuard.Helpers;

/// <summary>
/// Scoped session coordinator combining in-memory JTI/lockout tracking with SQLite-persisted refresh tokens.
/// </summary>
public class SessionService(DatabaseContext context, SessionCacheService cacheService)
{
    public void StoreActiveAccessToken(int userId, string jti, TimeSpan expiry)
    {
        cacheService.StoreActiveAccessToken(userId, jti, expiry);
    }

    public bool IsAccessTokenActive(int userId, string jti)
    {
        return cacheService.IsAccessTokenActive(userId, jti);
    }

    public void RevokeAccessToken(int userId, string jti)
    {
        cacheService.RevokeAccessToken(userId, jti);
    }

    public async Task<string> CreateRefreshToken(int userId, TimeSpan expiry)
    {
        var now = DateTime.UtcNow;

        // Prune already-expired refresh tokens via EF Core bulk delete to keep the table bounded.
        await context.RefreshTokens
            .Where(token => token.ExpiresAt <= now)
            .ExecuteDeleteAsync();

        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Base64Url.EncodeToString(secretBytes);
        var tokenHash = HashSecret(secret);

        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = now.Add(expiry),
            CreatedAt = now
        });
        await context.SaveChangesAsync();

        return $"{userId}.{secret}";
    }

    public async Task<(bool IsValid, int UserId)> ConsumeRefreshToken(string refreshToken)
    {
        if (!TryParseRefreshToken(refreshToken, out var userId, out var secret))
        {
            return (false, 0);
        }

        var tokenHash = HashSecret(secret);
        var now = DateTime.UtcNow;

        var deleted = await context.RefreshTokens
            .Where(token => token.UserId == userId && token.TokenHash == tokenHash && token.ExpiresAt > now)
            .ExecuteDeleteAsync();

        return deleted == 1 ? (true, userId) : (false, 0);
    }

    public async Task RevokeRefreshToken(string refreshToken)
    {
        if (!TryParseRefreshToken(refreshToken, out var userId, out var secret))
        {
            return;
        }

        var tokenHash = HashSecret(secret);

        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.TokenHash == tokenHash)
            .ExecuteDeleteAsync();
    }

    public void IncreaseUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        cacheService.IncreaseUserLoginRetries(userId, clientAddress);
    }

    public void ClearUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        cacheService.ClearUserLoginRetries(userId, clientAddress);
    }

    public bool IsLockedUser(int userId, IPAddress? clientAddress)
    {
        return cacheService.IsLockedUser(userId, clientAddress);
    }

    public void ClearAllUserLoginRetries(int userId)
    {
        cacheService.ClearAllUserLoginRetries(userId);
    }

    public async Task DeleteUserEntries(int userId)
    {
        cacheService.RevokeUserAccessTokensAndRetries(userId);

        await context.RefreshTokens
            .Where(token => token.UserId == userId)
            .ExecuteDeleteAsync();
    }

    internal static string HashSecret(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hash);
    }

    private static bool TryParseRefreshToken(string refreshToken, out int userId, out string secret)
    {
        userId = 0;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var separatorIndex = refreshToken.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == refreshToken.Length - 1)
        {
            return false;
        }

        var userIdSpan = refreshToken.AsSpan(0, separatorIndex);
        if (!int.TryParse(userIdSpan, out userId) || userId <= 0)
        {
            return false;
        }

        secret = refreshToken[(separatorIndex + 1)..];
        return !secret.Contains('.');
    }
}
