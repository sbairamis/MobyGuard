using System.Security.Cryptography;
using System.Text;

namespace MobyGuard.Auth;

public class SetupTokenService(ILogger<SetupTokenService> logger)
{
    private readonly Lock _lock = new();
    private string? _token;

    public string? CurrentToken
    {
        get
        {
            lock (_lock)
            {
                return _token;
            }
        }
    }

    public string EnsureTokenGenerated()
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_token))
            {
                return _token;
            }

            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

            logger.LogInformation(
                "Initial admin setup is required. Use this one-time setup token to claim the administrator account: {SetupToken}",
                _token);

            return _token;
        }
    }

    public bool VerifyToken(string? providedToken)
    {
        if (string.IsNullOrWhiteSpace(providedToken))
        {
            return false;
        }

        string? expected;
        lock (_lock)
        {
            expected = _token;
        }

        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedToken.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public void InvalidateToken()
    {
        lock (_lock)
        {
            _token = null;
        }
    }
}
