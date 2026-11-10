using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MobyGuardApi.Configuration;
using MobyGuardApi.Database.Dtos.TokenDtos;

namespace MobyGuardApi.Auth;

public sealed class RegistryTokenSigner : IDisposable
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private readonly RegistryTokenOptions _options;
    private readonly X509Certificate2 _certificate;
    private readonly RSA _privateKey;
    private readonly string _keyId;
    private readonly string _certBase64Der;

    public RegistryTokenSigner(IOptions<RegistryTokenOptions> options)
    {
        _options = options.Value;

        if (!File.Exists(_options.CertificatePath))
        {
            throw new FileNotFoundException($"Registry token certificate file not found at '{_options.CertificatePath}'.", _options.CertificatePath);
        }

        if (!File.Exists(_options.PrivateKeyPath))
        {
            throw new FileNotFoundException($"Registry token private key file not found at '{_options.PrivateKeyPath}'.", _options.PrivateKeyPath);
        }

        _certificate = X509Certificate2.CreateFromPemFile(_options.CertificatePath, _options.PrivateKeyPath);
        _privateKey = _certificate.GetRSAPrivateKey()
                      ?? throw new InvalidOperationException("The configured registry token certificate must have an RSA private key.");

        _keyId = ComputeLibtrustKeyId(_certificate);
        _certBase64Der = Convert.ToBase64String(_certificate.RawData);
    }

    public string KeyId => _keyId;

    public (string Token, int ExpiresInSeconds, DateTime IssuedAtUtc) CreateToken(
        string subject,
        string? requestedService,
        List<RegistryAccessEntryDto> accessEntries)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresInSeconds = _options.TokenExpiryMinutes * 60;
        var expiresAt = issuedAt.AddSeconds(expiresInSeconds);
        var audience = !string.IsNullOrWhiteSpace(requestedService) ? requestedService : _options.Service;

        var header = new Dictionary<string, object>
        {
            ["typ"] = "JWT",
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = _keyId,
            ["x5c"] = new[] { _certBase64Der }
        };

        var issuedUnix = new DateTimeOffset(issuedAt).ToUnixTimeSeconds();
        var expiresUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["sub"] = subject,
            ["aud"] = audience,
            ["exp"] = expiresUnix,
            ["nbf"] = issuedUnix,
            ["iat"] = issuedUnix,
            ["jti"] = Guid.NewGuid().ToString(),
            ["access"] = accessEntries
        };

        var headerBase64 = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadBase64 = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerBase64}.{payloadBase64}";

        var signatureBytes = _privateKey.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var signatureBase64 = Base64UrlEncoder.Encode(signatureBytes);

        return ($"{signingInput}.{signatureBase64}", expiresInSeconds, issuedAt);
    }

    public RegistryJwksResponseDto GetJwks()
    {
        using var publicRsa = _certificate.GetRSAPublicKey()!;
        var parameters = publicRsa.ExportParameters(false);

        return new RegistryJwksResponseDto
        {
            Keys =
            [
                new RegistryJwkDto
                {
                    Kty = "RSA",
                    Use = "sig",
                    Alg = SecurityAlgorithms.RsaSha256,
                    Kid = _keyId,
                    N = Base64UrlEncoder.Encode(parameters.Modulus!),
                    E = Base64UrlEncoder.Encode(parameters.Exponent!),
                    X5c = [_certBase64Der]
                }
            ]
        };
    }

    // Computes the Docker/Distribution libtrust key identifier from the certificate's SubjectPublicKeyInfo DER bytes.
    public static string ComputeLibtrustKeyId(X509Certificate2 certificate)
    {
        var spkiDer = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        var sha256 = SHA256.HashData(spkiDer);

        // Truncate SHA-256 to the first 240 bits (30 bytes), encode in Base32 (48 chars), and group by 4 chars separated by ':'.
        var base32 = EncodeBase32(sha256.AsSpan(0, 30));
        var sb = new StringBuilder(59);

        for (var i = 0; i < base32.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                sb.Append(':');
            }

            sb.Append(base32[i]);
        }

        return sb.ToString();
    }

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        var outputLength = (data.Length * 8 + 4) / 5;
        var result = new StringBuilder(outputLength);

        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;

        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xFF;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int index = 0x1F & (buffer >> (bitsLeft - 5));
            bitsLeft -= 5;
            result.Append(Base32Alphabet[index]);
        }

        return result.ToString();
    }

    public void Dispose()
    {
        _privateKey.Dispose();
        _certificate.Dispose();
    }
}
