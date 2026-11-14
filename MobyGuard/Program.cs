using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MobyGuard.Auth;
using MobyGuard.Configuration;
using MobyGuard.Database.Context;
using MobyGuard.Extensions;
using MobyGuard.Global;
using MobyGuard.HealthChecks;
using MobyGuard.Helpers;
using MobyGuard.Pagination;
using MobyGuard.Repos;
using MobyGuard.Seeders;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' could not be found.");

builder.Services.AddValidatedOptions<JwtOptions>();
builder.Services.AddValidatedOptions<RegistryTokenOptions>();
builder.Services.AddValidatedOptions<HostingOptions>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedPrefix;
    options.ForwardLimit = 1;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddSingleton<RegistryTokenSigner>();

builder.Services.AddHttpClient("DockerRegistry", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddScoped<AuditLogRepository>();
builder.Services.AddScoped<RegistryRepository>();
builder.Services.AddScoped<RepositoryPermissionRepository>();
builder.Services.AddScoped<TokenRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<UserTypeRepository>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database")
    .AddCheck<RegistryKeyHealthCheck>("RegistrySigningKey");

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SessionCacheService>();
builder.Services.AddScoped<SessionService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Value.Issuer,
            ValidAudience = jwtOptions.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtOptions.Value.SecurityKey)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var subClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var jtiClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

                if (!int.TryParse(subClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
                {
                    context.Fail("Invalid token claims.");
                    return Task.CompletedTask;
                }

                var sessionCache = context.HttpContext.RequestServices.GetRequiredService<SessionCacheService>();

                if (!sessionCache.IsAccessTokenActive(userId, jtiClaim))
                {
                    context.Fail("Token has been revoked.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddTransient<IUriService>(options =>
{
    var accessor = options.GetRequiredService<IHttpContextAccessor>();

    var request = accessor?.HttpContext?.Request;

    var uri = string.Concat(request?.Scheme, "://", request?.Host.ToUriComponent(), request?.PathBase.ToUriComponent());

    return new UriService(uri);
});

builder.Services.AddDbContext<DatabaseContext>(options => options.UseSqlite(connectionString));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.Services.GetRequiredService<IStartupValidator>().Validate();

app.Services.GetRequiredService<RegistryTokenSigner>();

app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseStatusCodePages();

app.MapHealthChecks("/api/health");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<DatabaseContext>();

    var activeConnectionString = dbContext.Database.GetConnectionString();

    EnsureSqliteDirectoryExists(activeConnectionString);

    dbContext.Database.Migrate();

    if (IsFileBasedSqlite(activeConnectionString))
    {
        dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    var retentionCutoff = DateTime.UtcNow.AddDays(-Variables.AuditLogRetentionDays);
    dbContext.AuditLogs.Where(log => log.CreatedAt < retentionCutoff).ExecuteDelete();

    DbInitializer.Initialize(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();

app.UseStaticFiles();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

var hostingOptions = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;

app.Run($"{hostingOptions.Scheme}://{hostingOptions.Ip}:{hostingOptions.Port}");

public partial class Program
{
    private static bool IsFileBasedSqlite(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);

        return sqliteBuilder.Mode != SqliteOpenMode.Memory
               && !string.IsNullOrWhiteSpace(sqliteBuilder.DataSource)
               && !string.Equals(sqliteBuilder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
               && !sqliteBuilder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSqliteDirectoryExists(string? connectionString)
    {
        if (!IsFileBasedSqlite(connectionString))
        {
            return;
        }

        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
        var fullPath = Path.GetFullPath(sqliteBuilder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
