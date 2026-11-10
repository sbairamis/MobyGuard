using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MobyGuardApi.Auth;
using MobyGuardApi.Configuration;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Dtos.UserDtos;
using MobyGuardApi.Database.Models;
using MobyGuardApi.Enumerators;
using MobyGuardApi.Helpers;
using MobyGuardApi.Seeders;
using Npgsql;
using Respawn;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private readonly RedisContainer _redisContainer;
    private DbConnection _dbConnection = default!;
    private Respawner _respawner = default!;

    private readonly string _certsDirectory = Path.Combine(Path.GetTempPath(), "mobyguard-tests", Guid.NewGuid().ToString("N"));
    private string _certPath = string.Empty;
    private string _keyPath = string.Empty;

    public CustomWebApplicationFactory()
    {
        _dbContainer = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("mobyguard_test")
            .WithUsername("olympia")
            .WithPassword("olympiarnd")
            .Build();

        _redisContainer = new RedisBuilder("redis:7-alpine")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_dbContainer.StartAsync(), _redisContainer.StartAsync());

        Directory.CreateDirectory(_certsDirectory);
        GenerateSelfSignedCert();

        // Start the app first: Program.cs applies the migrations, and Respawn needs the tables to exist.
        _ = Server;

        _dbConnection = new NpgsqlConnection(_dbContainer.GetConnectionString());

        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = new Respawn.Graph.Table[] { "__EFMigrationsHistory" }
        });
    }

    private void GenerateSelfSignedCert()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=mobyguard-token-issuer", ecdsa, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));

        _certPath = Path.Combine(_certsDirectory, "registry-auth.crt");
        _keyPath = Path.Combine(_certsDirectory, "registry-auth.key");

        File.WriteAllText(_certPath, cert.ExportCertificatePem());
        File.WriteAllText(_keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    public async Task ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(_dbConnection);

        await _redisContainer.ExecAsync(["redis-cli", "FLUSHALL"]);

        using var scope = Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

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
                FirstName = name,
                LastName = name,
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
            var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
            var redisHelper = scope.ServiceProvider.GetRequiredService<RedisHelper>();
            var jti = Guid.NewGuid().ToString("N");
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtOptions.SecurityKey));
            var token = new JwtSecurityToken(
                issuer: jwtOptions.Issuer,
                audience: jwtOptions.Audience,
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, ((int)userType).ToString()),
                    new Claim(JwtRegisteredClaimNames.UniqueName, username),
                    new Claim("role", Roles.Service),
                    new Claim(JwtRegisteredClaimNames.Jti, jti)
                },
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            await redisHelper.StoreActiveAccessToken((int)userType, jti, TimeSpan.FromMinutes(15));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        var response = await client.PostAsJsonAsync(
            "/api/User/login",
            new LoginUserDto { Username = username, Password = username.ToLowerInvariant() },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: TestContext.Current.CancellationToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:RedisConnection", _redisContainer.GetConnectionString());
        builder.UseSetting("RegistryTokenOptions:CertificatePath", _certPath);
        builder.UseSetting("RegistryTokenOptions:PrivateKeyPath", _keyPath);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<DatabaseContext>));
            services.RemoveAll(typeof(DatabaseContext));

            var connectionString = _dbContainer.GetConnectionString();

            services.AddDbContext<DatabaseContext>(options =>
            {
                options.UseNpgsql(connectionString);
            });

            services.RemoveAll(typeof(IConnectionMultiplexer));

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString()));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_dbConnection != null)
        {
            await _dbConnection.DisposeAsync();
        }

        await _dbContainer.DisposeAsync();

        await _redisContainer.DisposeAsync();

        if (Directory.Exists(_certsDirectory))
        {
            Directory.Delete(_certsDirectory, recursive: true);
        }
    }
}
