using System.Net;

namespace MobyGuard.Helpers;

/// <summary>
/// Scoped session coordinator managing in-memory active session JTIs and login lockout tracking.
/// </summary>
public class SessionService(SessionCacheService cacheService)
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

    public void IncreaseUnknownUserLoginRetries(string? username, IPAddress? clientAddress)
    {
        cacheService.IncreaseUnknownUserLoginRetries(username, clientAddress);
    }

    public bool IsLockedUnknownUser(string? username, IPAddress? clientAddress)
    {
        return cacheService.IsLockedUnknownUser(username, clientAddress);
    }

    public void ClearAllUserLoginRetries(int userId)
    {
        cacheService.ClearAllUserLoginRetries(userId);
    }

    public void DeleteUserEntries(int userId)
    {
        cacheService.RevokeUserAccessTokensAndRetries(userId);
    }
}
