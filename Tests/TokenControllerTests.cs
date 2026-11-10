using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MobyGuardApi.Classes;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Dtos.RepositoryPermissionDtos;
using MobyGuardApi.Database.Dtos.TokenDtos;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Global;

namespace Tests;

public class TokenControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public TokenControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await _factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient CreateBasicAuthClient(string username, string password, string? clientIp = null)
    {
        var client = _factory.CreateClient();
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);

        if (clientIp is not null)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        }

        return client;
    }

    private async Task GrantPermissionAsync(int userId, string pattern, bool canPull, bool canPush, bool canDelete = false)
    {
        var adminClient = await _factory.CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes.Admin);
        var response = await adminClient.PostAsJsonAsync(
            "/api/RepositoryPermission",
            new AddRepositoryPermissionDto
            {
                UserId = userId,
                RepositoryPattern = pattern,
                CanPull = canPull,
                CanPush = canPush,
                CanDelete = canDelete
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static (JsonDocument Header, JsonDocument Payload, string SigningInput, byte[] Signature) ParseJwtParts(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var headerBytes = Base64UrlEncoder.DecodeBytes(parts[0]);
        var payloadBytes = Base64UrlEncoder.DecodeBytes(parts[1]);
        var signatureBytes = Base64UrlEncoder.DecodeBytes(parts[2]);

        return (
            JsonDocument.Parse(headerBytes),
            JsonDocument.Parse(payloadBytes),
            $"{parts[0]}.{parts[1]}",
            signatureBytes);
    }

    [Fact]
    public async Task GetToken_DockerLoginWithoutScope_ReturnsValidSignedJwt()
    {
        var client = CreateBasicAuthClient("User", "user");

        var response = await client.GetAsync("/api/Token?service=registry", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegistryTokenResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.Equal(body.Token, body.AccessToken);
        Assert.Equal(900, body.ExpiresIn);

        var (header, payload, signingInput, signature) = ParseJwtParts(body.Token);
        using (header)
        using (payload)
        {
            Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
            Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());

            var kid = header.RootElement.GetProperty("kid").GetString();
            Assert.False(string.IsNullOrWhiteSpace(kid));
            Assert.Equal(12, kid!.Split(':').Length);

            var x5c = header.RootElement.GetProperty("x5c");
            Assert.Equal(JsonValueKind.Array, x5c.ValueKind);
            var certDer = Convert.FromBase64String(x5c[0].GetString()!);
            using var cert = X509CertificateLoader.LoadCertificate(certDer);
            using var rsaPublic = cert.GetRSAPublicKey()!;

            var signatureValid = rsaPublic.VerifyData(
                Encoding.ASCII.GetBytes(signingInput),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            Assert.True(signatureValid);

            Assert.Equal("mobyguard-token-issuer", payload.RootElement.GetProperty("iss").GetString());
            Assert.Equal("User", payload.RootElement.GetProperty("sub").GetString());
            Assert.Equal("registry", payload.RootElement.GetProperty("aud").GetString());
            Assert.Equal(0, payload.RootElement.GetProperty("access").GetArrayLength());
        }
    }

    [Fact]
    public async Task GetToken_UserWithPullPermission_GrantsPullAndFiltersOutPush()
    {
        await GrantPermissionAsync((int)PrimaryKeys.UserTypes.User, "olympia/firmwares-api", canPull: true, canPush: false);

        var client = CreateBasicAuthClient("User", "user");

        // Request both pull and push on a pull-only repository
        var response = await client.GetAsync(
            "/api/Token?service=registry&scope=repository:olympia/firmwares-api:pull,push",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegistryTokenResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        var (_, payload, _, _) = ParseJwtParts(body!.Token);
        using (payload)
        {
            var access = payload.RootElement.GetProperty("access");
            Assert.Equal(1, access.GetArrayLength());

            var entry = access[0];
            Assert.Equal("repository", entry.GetProperty("type").GetString());
            Assert.Equal("olympia/firmwares-api", entry.GetProperty("name").GetString());

            var actions = entry.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Single(actions);
            Assert.Equal("pull", actions[0]);
        }
    }

    [Fact]
    public async Task GetToken_UserWithoutPermissionOnRepo_ReturnsEmptyActionsInAccessClaim()
    {
        await GrantPermissionAsync((int)PrimaryKeys.UserTypes.User, "olympia/allowed-repo", canPull: true, canPush: false);

        var client = CreateBasicAuthClient("User", "user");

        var response = await client.GetAsync(
            "/api/Token?service=registry&scope=repository:olympia/secret-repo:pull",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegistryTokenResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        var (_, payload, _, _) = ParseJwtParts(body!.Token);
        using (payload)
        {
            var entry = payload.RootElement.GetProperty("access")[0];
            Assert.Equal("olympia/secret-repo", entry.GetProperty("name").GetString());
            Assert.Equal(0, entry.GetProperty("actions").GetArrayLength());
        }
    }

    [Fact]
    public async Task GetToken_WildcardNamespacePermission_MatchesAllReposInNamespace()
    {
        await GrantPermissionAsync((int)PrimaryKeys.UserTypes.Service, "olympia/*", canPull: true, canPush: true);

        var client = CreateBasicAuthClient("Service", "service");

        var response = await client.GetAsync(
            "/api/Token?service=registry&scope=repository:olympia/iot-gateway:pull,push&scope=repository:external/other:pull",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegistryTokenResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        var (_, payload, _, _) = ParseJwtParts(body!.Token);
        using (payload)
        {
            var access = payload.RootElement.GetProperty("access");
            Assert.Equal(2, access.GetArrayLength());

            var olympiaActions = access[0].GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Equal(["pull", "push"], olympiaActions);

            var externalActions = access[1].GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Empty(externalActions);
        }
    }

    [Fact]
    public async Task GetToken_AdminUser_AutomaticallyGrantedFullAccessAndCatalog()
    {
        var client = CreateBasicAuthClient("Admin", "admin");

        var response = await client.GetAsync(
            "/api/Token?service=registry&scope=repository:any/repo:pull,push,delete&scope=registry:catalog:*",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RegistryTokenResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        var (_, payload, _, _) = ParseJwtParts(body!.Token);
        using (payload)
        {
            var access = payload.RootElement.GetProperty("access");
            Assert.Equal(2, access.GetArrayLength());

            var repoActions = access[0].GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Equal(["pull", "push", "delete"], repoActions);

            var catalogActions = access[1].GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Equal(["*"], catalogActions);
        }
    }

    [Fact]
    public async Task GetToken_InvalidCredentials_ReturnsUnauthorized()
    {
        var client = CreateBasicAuthClient("User", "wrong-password");

        var response = await client.GetAsync("/api/Token?service=registry", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ResponseMessages.InvalidCredentials, problem!.Detail);
    }

    [Fact]
    public async Task GetToken_MissingAuthorizationHeader_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Token?service=registry", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_AfterMaxFailedAttempts_ReturnsTooManyRequests()
    {
        for (var i = 0; i < Variables.LoginMaxRetries; i++)
        {
            using var badClient = CreateBasicAuthClient("User", "wrong", "203.0.113.55");
            await badClient.GetAsync("/api/Token?service=registry", TestContext.Current.CancellationToken);
        }

        using var validClient = CreateBasicAuthClient("User", "user", "203.0.113.55");
        var response = await validClient.GetAsync("/api/Token?service=registry", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task GetJwks_ReturnsPublicKeyMatchingTokenKid()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Token/jwks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jwks = await response.Content.ReadFromJsonAsync<RegistryJwksResponseDto>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(jwks);
        Assert.Single(jwks.Keys);
        Assert.Equal("RSA", jwks.Keys[0].Kty);
        Assert.Equal("RS256", jwks.Keys[0].Alg);
        Assert.False(string.IsNullOrWhiteSpace(jwks.Keys[0].Kid));
    }
}
