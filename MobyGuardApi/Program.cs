using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MobyGuardApi.Auth;
using MobyGuardApi.Configuration;
using MobyGuardApi.Database.Context;
using MobyGuardApi.Extensions;
using MobyGuardApi.HealthChecks;
using MobyGuardApi.Helpers;
using MobyGuardApi.Pagination;
using MobyGuardApi.Repos;
using MobyGuardApi.Seeders;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' could not be found.");

var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection")
                            ?? throw new InvalidOperationException("Connection string 'RedisConnection' could not be found.");

builder.Services.AddValidatedOptions<JwtOptions>();
builder.Services.AddValidatedOptions<RegistryTokenOptions>();
builder.Services.AddValidatedOptions<HostingOptions>();
builder.Services.AddValidatedOptions<CorsPolicyOptions>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedPrefix;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddSingleton<RegistryTokenSigner>();

builder.Services.AddScoped<AuditLogRepository>();
builder.Services.AddScoped<RepositoryPermissionRepository>();
builder.Services.AddScoped<TokenRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<UserTypeRepository>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database")
    .AddCheck<RedisHealthCheck>("Redis")
    .AddCheck<RegistryKeyHealthCheck>("RegistrySigningKey");

builder.Services.AddScoped<RedisHelper>();

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
    });

builder.Services.AddAuthorization();

builder.Services.AddCors();

builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<CorsPolicyOptions>>((corsOptions, corsPolicyOptions) =>
    {
        corsOptions.AddDefaultPolicy(policy => policy
            .WithOrigins(corsPolicyOptions.Value.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromHours(1)));
    });

builder.Services.AddHttpContextAccessor();

builder.Services.AddTransient<IUriService>(options =>
{
    var accessor = options.GetRequiredService<IHttpContextAccessor>();

    var request = accessor?.HttpContext?.Request;

    var uri = string.Concat(request?.Scheme, "://", request?.Host.ToUriComponent(), request?.PathBase.ToUriComponent());

    return new UriService(uri);
});

builder.Services.AddDbContext<DatabaseContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

    return ConnectionMultiplexer.Connect(redisOptions);
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.Services.GetRequiredService<IStartupValidator>().Validate();

app.Services.GetRequiredService<RegistryTokenSigner>();

app.Services.GetRequiredService<IConnectionMultiplexer>();

app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseStatusCodePages();

app.MapHealthChecks("/api/health");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<DatabaseContext>();

    dbContext.Database.Migrate();

    DbInitializer.Initialize(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

var hostingOptions = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value;

app.Run($"{hostingOptions.Scheme}://{hostingOptions.Ip}:{hostingOptions.Port}");

public partial class Program { }
