using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using MobyGuard.Global;

namespace MobyGuard.Helpers;

/// <summary>
/// Singleton in-memory store for short-lived active access token JTIs and per-user login lockout counters.
/// </summary>
public sealed class SessionCacheService(IMemoryCache cache)
{
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(Variables.LoginRetriesExpiryMinutes);

    private readonly ConcurrentDictionary<int, CancellationTokenSource> _tokenCancellationSources = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _attemptCancellationSources = new();
    private readonly Lock _ctsLock = new();
    private readonly Lock _retryLock = new();

    public void StoreActiveAccessToken(int userId, string jti, TimeSpan expiry)
    {
        if (expiry <= TimeSpan.Zero || string.IsNullOrWhiteSpace(jti))
        {
            return;
        }

        var token = GetUserCancellationToken(userId, _tokenCancellationSources);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        };
        options.AddExpirationToken(new CancellationChangeToken(token));

        cache.Set(BuildActiveTokenKey(userId, jti), true, options);
    }

    public bool IsAccessTokenActive(int userId, string jti)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        return cache.TryGetValue(BuildActiveTokenKey(userId, jti), out _);
    }

    public void RevokeAccessToken(int userId, string jti)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(jti))
        {
            return;
        }

        cache.Remove(BuildActiveTokenKey(userId, jti));
    }

    public void IncreaseUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        var key = BuildLoginAttemptsKey(userId, clientAddress);

        lock (_retryLock)
        {
            var token = GetUserCancellationToken(userId, _attemptCancellationSources);
            var currentCount = cache.TryGetValue(key, out int existing) ? existing : 0;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = RetryWindow
            };
            options.AddExpirationToken(new CancellationChangeToken(token));

            cache.Set(key, currentCount + 1, options);
        }
    }

    public void ClearUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        var key = BuildLoginAttemptsKey(userId, clientAddress);

        lock (_retryLock)
        {
            cache.Remove(key);
        }
    }

    public bool IsLockedUser(int userId, IPAddress? clientAddress)
    {
        var key = BuildLoginAttemptsKey(userId, clientAddress);

        lock (_retryLock)
        {
            return cache.TryGetValue(key, out int attempts) && attempts >= Variables.LoginMaxRetries;
        }
    }

    public void ClearAllUserLoginRetries(int userId)
    {
        lock (_retryLock)
        {
            RotateUserCancellationSource(userId, _attemptCancellationSources);
        }
    }

    public void RevokeUserAccessTokensAndRetries(int userId)
    {
        RotateUserCancellationSource(userId, _tokenCancellationSources);

        lock (_retryLock)
        {
            RotateUserCancellationSource(userId, _attemptCancellationSources);
        }
    }

    internal void ClearAll()
    {
        lock (_ctsLock)
        {
            foreach (var cts in _tokenCancellationSources.Values)
            {
                cts.Cancel();
            }

            foreach (var cts in _attemptCancellationSources.Values)
            {
                cts.Cancel();
            }

            _tokenCancellationSources.Clear();
            _attemptCancellationSources.Clear();
        }

        if (cache is MemoryCache memoryCache)
        {
            memoryCache.Clear();
        }
    }

    private CancellationToken GetUserCancellationToken(
        int userId,
        ConcurrentDictionary<int, CancellationTokenSource> map)
    {
        lock (_ctsLock)
        {
            var cts = map.GetOrAdd(userId, _ => new CancellationTokenSource());
            if (cts.IsCancellationRequested)
            {
                cts = new CancellationTokenSource();
                map[userId] = cts;
            }

            return cts.Token;
        }
    }

    private void RotateUserCancellationSource(
        int userId,
        ConcurrentDictionary<int, CancellationTokenSource> map)
    {
        CancellationTokenSource? previous = null;

        lock (_ctsLock)
        {
            if (map.TryGetValue(userId, out previous))
            {
                map[userId] = new CancellationTokenSource();
            }
        }

        // Cancel outside the map lock and avoid disposing while concurrent readers may still register callbacks.
        previous?.Cancel();
    }

    private static string BuildActiveTokenKey(int userId, string jti)
    {
        return $"active-token:{userId}:{jti}";
    }

    private static string BuildLoginAttemptsKey(int userId, IPAddress? clientAddress)
    {
        return $"login-attempts:{userId}:{NormalizeIpAddress(clientAddress)}";
    }

    private static string NormalizeIpAddress(IPAddress? ipAddress)
    {
        if (ipAddress is null)
        {
            return "unknown";
        }

        if (ipAddress.IsIPv4MappedToIPv6)
        {
            return ipAddress.MapToIPv4().ToString();
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ipAddress.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return $"{new IPAddress(bytes)}/64";
        }

        return ipAddress.ToString();
    }
}
