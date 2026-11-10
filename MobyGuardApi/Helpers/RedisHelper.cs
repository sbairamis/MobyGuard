using StackExchange.Redis;
using System.Net;
using System.Net.Sockets;

namespace MobyGuardApi.Helpers;

public class RedisHelper
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;

    private readonly IDatabase _redisDB;

    private string UserLoginAttemptsKeyNamespace { get; } = "User:LoginAttempts";

    public RedisHelper(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;

        _redisDB = connectionMultiplexer.GetDatabase();
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

    // Clear user's failed attempts from every address, so a password reset also unlocks the account everywhere.
    public async Task DeleteUserEntries(int userId)
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
