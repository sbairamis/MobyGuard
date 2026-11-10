using StackExchange.Redis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace MobyGuard.Helpers;

public class RedisHelper
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    private readonly IDatabase _redisDB;

    private string KeyPrefix { get; } = "com.mobeguard";

    private string UserLoginAttemptsKeyNamespace => $"{KeyPrefix}:User:LoginAttempts";

    private string UserActiveTokensKeyNamespace => $"{KeyPrefix}:User:ActiveTokens";

    private string UserRefreshTokensKeyNamespace => $"{KeyPrefix}:User:RefreshTokens";

    public RedisHelper(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;

        _redisDB = connectionMultiplexer.GetDatabase();
    }

    public async Task StoreActiveAccessToken(int userId, string jti, TimeSpan expiry)
    {
        var key = ActiveTokenKey(userId, jti);

        await _redisDB.StringSetAsync(key, "1", expiry);
    }

    public async Task<bool> IsAccessTokenActive(int userId, string jti)
    {
        var key = ActiveTokenKey(userId, jti);

        return await _redisDB.KeyExistsAsync(key);
    }

    public async Task RevokeAccessToken(int userId, string jti)
    {
        var key = ActiveTokenKey(userId, jti);

        await _redisDB.KeyDeleteAsync(key);
    }

    public async Task<string> CreateRefreshToken(int userId, TimeSpan expiry)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var key = RefreshTokenKey(userId, secret);

        await _redisDB.StringSetAsync(key, "1", expiry);

        return $"{userId}.{secret}";
    }

    public async Task<(bool IsValid, int UserId)> ConsumeRefreshToken(string refreshToken)
    {
        if (!TryParseRefreshToken(refreshToken, out var userId, out var secret))
        {
            return (false, 0);
        }

        var key = RefreshTokenKey(userId, secret);
        var val = await _redisDB.StringGetDeleteAsync(key);

        if (val.IsNullOrEmpty)
        {
            return (false, 0);
        }

        return (true, userId);
    }

    public async Task RevokeRefreshToken(string refreshToken)
    {
        if (!TryParseRefreshToken(refreshToken, out var userId, out var secret))
        {
            return;
        }

        var key = RefreshTokenKey(userId, secret);

        await _redisDB.KeyDeleteAsync(key);
    }

    public async Task IncreaseUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        var entryKey = LoginAttemptsKey(userId, clientAddress);

        await _redisDB.StringIncrementAsync(entryKey, 1);

        var expiry = TimeSpan.FromMinutes(Global.Variables.LoginRetriesExpiryMinutes);

        await _redisDB.KeyExpireAsync(entryKey, expiry);
    }

    public async Task ClearUserLoginRetries(int userId, IPAddress? clientAddress)
    {
        var entryKey = LoginAttemptsKey(userId, clientAddress);

        await _redisDB.KeyDeleteAsync(entryKey);
    }

    public async Task<bool> IsLockedUser(int userId, IPAddress? clientAddress)
    {
        var entryKey = LoginAttemptsKey(userId, clientAddress);

        var retriesValue = await _redisDB.StringGetAsync(entryKey);

        if (retriesValue.IsNullOrEmpty)
        {
            return false;
        }

        var currentRetries = (int)retriesValue;

        return currentRetries >= Global.Variables.LoginMaxRetries;
    }

    public async Task ClearAllUserLoginRetries(int userId)
    {
        var pattern = $"{UserLoginAttemptsKeyNamespace}:{userId}:*";

        var keys = new List<RedisKey>();

        foreach (var server in _connectionMultiplexer.GetServers().Where(server => !server.IsReplica))
        {
            await foreach (var key in server.KeysAsync(_redisDB.Database, pattern))
            {
                keys.Add(key);
            }
        }

        if (keys.Count > 0)
        {
            await _redisDB.KeyDeleteAsync(keys.ToArray());
        }
    }

    // Clear user's failed attempts, active access tokens, and refresh tokens across all sessions.
    public async Task DeleteUserEntries(int userId)
    {
        var patterns = new[]
        {
            $"{UserLoginAttemptsKeyNamespace}:{userId}:*",
            $"{UserActiveTokensKeyNamespace}:{userId}:*",
            $"{UserRefreshTokensKeyNamespace}:{userId}:*"
        };

        var keys = new List<RedisKey>();

        foreach (var server in _connectionMultiplexer.GetServers().Where(server => !server.IsReplica))
        {
            foreach (var pattern in patterns)
            {
                await foreach (var key in server.KeysAsync(_redisDB.Database, pattern))
                {
                    keys.Add(key);
                }
            }
        }

        if (keys.Count > 0)
        {
            await _redisDB.KeyDeleteAsync(keys.ToArray());
        }
    }

    private string ActiveTokenKey(int userId, string jti)
    {
        return $"{UserActiveTokensKeyNamespace}:{userId}:{jti}";
    }

    private string RefreshTokenKey(int userId, string secret)
    {
        return $"{UserRefreshTokensKeyNamespace}:{userId}:{secret}";
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
        if (separatorIndex <= 0 || separatorIndex >= refreshToken.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(refreshToken.AsSpan(0, separatorIndex), out userId) || userId <= 0)
        {
            return false;
        }

        secret = refreshToken[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(secret);
    }

    // Failed logins are counted per account and client address (or /64 prefix for IPv6).
    private string LoginAttemptsKey(int userId, IPAddress? clientAddress)
    {
        return $"{UserLoginAttemptsKeyNamespace}:{userId}:{NormalizeClientAddress(clientAddress)}";
    }

    private static string NormalizeClientAddress(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString();
        }

        return address.ToString();
    }
}
