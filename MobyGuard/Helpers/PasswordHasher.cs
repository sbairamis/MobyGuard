using System.Security.Cryptography;
using System.Text;

namespace MobyGuard.Helpers;

public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    public static string GenerateSalt()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSizeBytes));
    }

    public static string Hash(string password, string salt)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt))
        {
            return string.Empty;
        }

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA512,
            HashSizeBytes);

        return Convert.ToBase64String(hashBytes);
    }

    public static bool Verify(string password, string salt, string expectedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var calculatedHash = Hash(password, salt);
        var actualBytes = Encoding.UTF8.GetBytes(calculatedHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
