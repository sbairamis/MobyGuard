using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
using MobyGuard.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' could not be found.");

builder.Services.AddValidatedOptions<RegistryTokenOptions>();
builder.Services.AddValidatedOptions<HostingOptions>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddOpenApi();

builder.Services.AddSingleton<RegistryTokenSigner>();

builder.Services.AddHttpClient("DockerRegistry", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient("OutboundWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuditLogRepository>();
builder.Services.AddScoped<OutboundWebhookDispatcher>();
builder.Services.AddScoped<RegistryRepository>();
builder.Services.AddScoped<RepositoryPermissionRepository>();
builder.Services.AddScoped<RetentionPolicyRepository>();
builder.Services.AddSingleton<RetentionPolicySchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RetentionPolicySchedulerService>());
builder.Services.AddScoped<TokenRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<UserTypeRepository>();
builder.Services.AddScoped<WebhookSubscriptionRepository>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database")
    .AddCheck<RegistryKeyHealthCheck>("RegistrySigningKey");

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SessionCacheService>();
builder.Services.AddSingleton<SetupTokenService>();
builder.Services.AddScoped<SessionService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "mobyguard_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromDays(Variables.SessionExpiryDays);
        options.SlidingExpiration = true;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnValidatePrincipal = context =>
            {
                var subClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var jtiClaim = context.Principal?.FindFirst("jti")?.Value;

                if (!int.TryParse(subClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
                {
                    context.RejectPrincipal();
                    return Task.CompletedTask;
                }

                var sessionCache = context.HttpContext.RequestServices.GetRequiredService<SessionCacheService>();

                if (!sessionCache.IsAccessTokenActive(userId, jtiClaim))
                {
                    context.RejectPrincipal();
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<DatabaseContext>(options => options.UseSqlite(connectionString));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.Services.GetRequiredService<IStartupValidator>().Validate();

app.Services.GetRequiredService<RegistryTokenSigner>();

var registryTokenOptions = app.Services.GetRequiredService<IOptions<RegistryTokenOptions>>().Value;
if (string.IsNullOrWhiteSpace(registryTokenOptions.WebhookSecret))
{
    app.Logger.LogWarning(
        "RegistryTokenOptions:WebhookSecret is not configured. The /api/v1/Registry/events endpoint is disabled until a secret is set.");
}

app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseStatusCodePages();

app.MapHealthChecks("/api/v1/health");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<DatabaseContext>();

    var dataSource = new SqliteConnectionStringBuilder(dbContext.Database.GetConnectionString()).DataSource;
    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory)
    {
        Directory.CreateDirectory(directory);
    }

    dbContext.Database.Migrate();

    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    DbInitializer.Initialize(dbContext);

    if (!dbContext.Users.Any())
    {
        services.GetRequiredService<SetupTokenService>().EnsureTokenGenerated();
    }
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
