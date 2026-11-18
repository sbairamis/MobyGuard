using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MobyGuard.Auth;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.UserDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;
using MobyGuard.Global;
using MobyGuard.Helpers;
using MobyGuard.Seeders;

namespace Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "mobyguard-tests", Guid.NewGuid().ToString("N"));
    private string _dbPath = string.Empty;
    private string _certPath = string.Empty;
    private string _keyPath = string.Empty;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_tempDirectory);
        _dbPath = Path.Combine(_tempDirectory, "mobyguard-test.db");
        GenerateSelfSignedCert();

        // Start the app so Program.cs creates and migrates the SQLite database.
        _ = Server;

        return ValueTask.CompletedTask;
    }

    private void GenerateSelfSignedCert()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=mobyguard-token-issuer", ecdsa, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));

        _certPath = Path.Combine(_tempDirectory, "registry-auth.crt");
        _keyPath = Path.Combine(_tempDirectory, "registry-auth.key");

        File.WriteAllText(_certPath, cert.ExportCertificatePem());
        File.WriteAllText(_keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    public FakeDockerRegistryHandler RegistryHandler { get; } = new();
    public FakeOutboundWebhookHandler OutboundWebhookHandler { get; } = new();

    public async Task ResetDatabaseAsync()
    {
        RegistryHandler.Reset();
        OutboundWebhookHandler.Reset();

        using var scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<SessionCacheService>().ClearAll();
        (scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>() as Microsoft.Extensions.Caching.Memory.MemoryCache)?.Compact(1.0);

        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        await dbContext.Database.ExecuteSqlRawAsync("""
            DELETE FROM "AuditLogs";
            DELETE FROM "RepositoryPermissions";
            DELETE FROM "RetentionPolicies";
            DELETE FROM "WebhookSubscriptions";
            DELETE FROM "Users";
            DELETE FROM "UserTypes";
            UPDATE "sqlite_sequence" SET "seq" = 0 WHERE "name" IN ('AuditLogs', 'RepositoryPermissions', 'RetentionPolicies', 'WebhookSubscriptions', 'Users', 'UserTypes');
            """);

        DbInitializer.Initialize(dbContext);

        SeedTestUsers(dbContext);
    }

    private static void SeedTestUsers(DatabaseContext context)
    {
        foreach (var userType in Enum.GetValues<PrimaryKeys.UserTypes>())
        {
            var salt = PasswordHasher.GenerateSalt();
            var name = userType.ToString();

            context.Users.Add(new User
            {
                Id = (int)userType,
                Username = name,
                Salt = salt,
                Password = PasswordHasher.Hash(name.ToLowerInvariant(), salt),
                IsActive = true,
                UserTypeId = (int)userType,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        context.SaveChanges();
        context.ResetIdentitySequence<User>();
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(PrimaryKeys.UserTypes userType)
    {
        var client = CreateClient();

        var username = userType.ToString();

        if (userType == PrimaryKeys.UserTypes.Service)
        {
            using var scope = Services.CreateScope();
            var sessionCache = scope.ServiceProvider.GetRequiredService<SessionCacheService>();
            var cookieOptions = scope.ServiceProvider
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);

            var jti = Guid.NewGuid().ToString("N");
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, ((int)userType).ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, Roles.Service),
                new Claim("jti", jti)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(Variables.SessionExpiryDays)
            };
            var ticket = new AuthenticationTicket(principal, props, CookieAuthenticationDefaults.AuthenticationScheme);
            var cookieValue = cookieOptions.TicketDataFormat.Protect(ticket);

            sessionCache.StoreActiveAccessToken((int)userType, jti, TimeSpan.FromDays(Variables.SessionExpiryDays));
            client.DefaultRequestHeaders.Add("Cookie", $"{cookieOptions.Cookie.Name}={cookieValue}");
            return client;
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/User/login",
            new LoginUserDto { Username = username, Password = username.ToLowerInvariant() },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
        builder.UseSetting("RegistryTokenOptions:CertificatePath", _certPath);
        builder.UseSetting("RegistryTokenOptions:PrivateKeyPath", _keyPath);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<DatabaseContext>));
            services.RemoveAll(typeof(DatabaseContext));

            services.AddDbContext<DatabaseContext>(options =>
            {
                options.UseSqlite($"Data Source={_dbPath}");
            });

            services.AddHttpClient("DockerRegistry")
                .ConfigurePrimaryHttpMessageHandler(() => RegistryHandler);

            services.AddHttpClient("OutboundWebhook")
                .ConfigurePrimaryHttpMessageHandler(() => OutboundWebhookHandler);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
