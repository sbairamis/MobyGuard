using System.Net;
using System.Security.Claims;
using MobyGuard.Database.Context;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;

namespace MobyGuard.Services;

public sealed record AuditActor(int? UserId, string Username);

/// <summary>
/// Single entry point for writing audit trail rows. Resolves the actor and client IP from the current
/// HTTP request unless provided explicitly, and adds the row to the current <see cref="DatabaseContext"/>
/// unit of work so it is persisted atomically with the change it describes (callers must SaveChanges).
/// Never pass secrets (passwords, tokens, full webhook URLs) into <c>details</c>.
/// </summary>
public class AuditService(DatabaseContext context, IHttpContextAccessor httpContextAccessor)
{
    public const string SystemActor = "system";
    public const string AnonymousActor = "anonymous";
    private const int MaxDetailsLength = 1000;

    public AuditLog Record(
        AuditLogEnums.Description description,
        string? target = null,
        int? targetUserId = null,
        string? repository = null,
        string? actions = null,
        string? details = null,
        AuditActor? actor = null,
        IPAddress? clientAddress = null)
    {
        var resolvedActor = actor ?? ResolveActor();
        var ip = clientAddress ?? httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        var normalizedDetails = details ?? string.Empty;

        var log = new AuditLog(
            username: resolvedActor.Username,
            repository: repository ?? string.Empty,
            actions: actions ?? string.Empty,
            description: description.ToString(),
            clientIp: FormatIp(ip))
        {
            ActorUserId = resolvedActor.UserId,
            Target = target ?? string.Empty,
            TargetUserId = targetUserId,
            Details = normalizedDetails.Length > MaxDetailsLength ? normalizedDetails[..MaxDetailsLength] : normalizedDetails
        };

        context.AuditLogs.Add(log);
        return log;
    }

    public AuditActor ResolveActor()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return new AuditActor(null, SystemActor);
        }

        var principal = httpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new AuditActor(null, AnonymousActor);
        }

        int? userId = int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        var username = principal.FindFirst(ClaimTypes.Name)?.Value;

        return new AuditActor(userId, string.IsNullOrWhiteSpace(username) ? AnonymousActor : username);
    }

    public static string FormatIp(IPAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }

    /// <summary>
    /// Builds a "field: before → after; ..." summary, including only fields whose value changed.
    /// </summary>
    public static string Diff(params (string Field, object? Before, object? After)[] changes)
    {
        var parts = changes
            .Where(c => !string.Equals(FormatValue(c.Before), FormatValue(c.After), StringComparison.Ordinal))
            .Select(c => $"{c.Field}: {FormatValue(c.Before)} → {FormatValue(c.After)}");

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Builds a "field: value; ..." summary for create/delete events.
    /// </summary>
    public static string Describe(params (string Field, object? Value)[] fields)
        => string.Join("; ", fields.Select(f => $"{f.Field}: {FormatValue(f.Value)}"));

    private static string FormatValue(object? value) => value switch
    {
        null => "—",
        bool b => b ? "true" : "false",
        string s when string.IsNullOrWhiteSpace(s) => "—",
        _ => value.ToString() ?? "—"
    };
}
