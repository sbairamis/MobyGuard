using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MobyGuard.Auth;
using MobyGuard.Classes;
using MobyGuard.Configuration;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.RegistryDtos;
using MobyGuard.Database.Dtos.TokenDtos;
using MobyGuard.Database.Models;
using MobyGuard.Enumerators;

namespace MobyGuard.Repos;

public class RegistryRepository(
    DatabaseContext context,
    RegistryTokenSigner tokenSigner,
    IHttpClientFactory httpClientFactory,
    IOptions<RegistryTokenOptions> registryOptions,
    ILogger<RegistryRepository> logger)
{
    private static readonly string[] ManifestAcceptHeaders =
    [
        "application/vnd.oci.image.index.v1+json",
        "application/vnd.oci.image.manifest.v1+json",
        "application/vnd.docker.distribution.manifest.list.v2+json",
        "application/vnd.docker.distribution.manifest.v2+json"
    ];

    private readonly RegistryTokenOptions _options = registryOptions.Value;

    public async Task<Result<List<RegistryRepositoryDto>>> GetRepositories(int callerId)
    {
        try
        {
            var (user, permissions, isAdmin) = await LoadCallerPermissions(callerId);
            if (user is null)
            {
                return Result<List<RegistryRepositoryDto>>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            using var client = CreateRegistryClient("mobyguard-ui", [
                new RegistryAccessEntryDto
                {
                    Type = "registry",
                    Name = "catalog",
                    Actions = ["*"]
                }
            ]);

            var allRepoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? nextUrl = "/v2/_catalog?n=100";
            const int maxPages = 1000;
            var pagesFetched = 0;

            while (!string.IsNullOrEmpty(nextUrl) && pagesFetched < maxPages)
            {
                pagesFetched++;
                using var response = await client.GetAsync(nextUrl);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Upstream Docker Registry returned {StatusCode} on {Url}", response.StatusCode, nextUrl);
                    return Result<List<RegistryRepositoryDto>>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var pageCount = 0;
                string? lastRepoInPage = null;

                if (doc.RootElement.TryGetProperty("repositories", out var reposElement)
                    && reposElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in reposElement.EnumerateArray())
                    {
                        var repoName = item.GetString();
                        if (string.IsNullOrWhiteSpace(repoName))
                        {
                            continue;
                        }

                        allRepoNames.Add(repoName);
                        lastRepoInPage = repoName;
                        pageCount++;
                    }
                }

                var linkNext = ExtractNextLink(response.Headers);
                if (!string.IsNullOrEmpty(linkNext))
                {
                    nextUrl = linkNext;
                }
                else if (pageCount >= 100 && !string.IsNullOrEmpty(lastRepoInPage))
                {
                    nextUrl = $"/v2/_catalog?n=100&last={Uri.EscapeDataString(lastRepoInPage)}";
                }
                else
                {
                    nextUrl = null;
                }
            }

            var repositories = new List<RegistryRepositoryDto>();
            foreach (var repoName in allRepoNames)
            {
                var (canPull, canPush, canDelete) = EvaluateUserRepoPermissions(repoName, isAdmin, permissions);
                if (!canPull)
                {
                    continue;
                }

                repositories.Add(new RegistryRepositoryDto
                {
                    Name = repoName,
                    CanPull = canPull,
                    CanPush = canPush,
                    CanDelete = canDelete
                });
            }

            return Result<List<RegistryRepositoryDto>>.Success(
                repositories.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Unable to reach upstream Docker Registry at {RegistryUrl}", _options.RegistryUrl);
            return Result<List<RegistryRepositoryDto>>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while listing registry repositories for user {CallerId}", callerId);
            return Result<List<RegistryRepositoryDto>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RegistryRepositoryTagsDto>> GetTags(string repository, int callerId)
    {
        try
        {
            var repoName = repository.Trim().Trim('/');
            var (user, permissions, isAdmin) = await LoadCallerPermissions(callerId);
            if (user is null)
            {
                return Result<RegistryRepositoryTagsDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            var (canPull, canPush, canDelete) = EvaluateUserRepoPermissions(repoName, isAdmin, permissions);
            if (!canPull)
            {
                return Result<RegistryRepositoryTagsDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.RegistryRepositoryAccessDenied);
            }

            using var client = CreateRegistryClient(user.Username, [
                new RegistryAccessEntryDto
                {
                    Type = "repository",
                    Name = repoName,
                    Actions = ["pull"]
                }
            ]);

            var allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? nextTagsUrl = $"/v2/{repoName}/tags/list?n=100";
            const int maxTagPages = 500;
            var tagPagesFetched = 0;

            while (!string.IsNullOrEmpty(nextTagsUrl) && tagPagesFetched < maxTagPages)
            {
                tagPagesFetched++;
                using var response = await client.GetAsync(nextTagsUrl);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result<RegistryRepositoryTagsDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var pageCount = 0;
                string? lastTagInPage = null;

                if (doc.RootElement.TryGetProperty("tags", out var tagsElement)
                    && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in tagsElement.EnumerateArray())
                    {
                        var tag = item.GetString();
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            allTags.Add(tag);
                            lastTagInPage = tag;
                            pageCount++;
                        }
                    }
                }

                var linkNext = ExtractNextLink(response.Headers);
                if (!string.IsNullOrEmpty(linkNext))
                {
                    nextTagsUrl = linkNext;
                }
                else if (pageCount >= 100 && !string.IsNullOrEmpty(lastTagInPage))
                {
                    nextTagsUrl = $"/v2/{repoName}/tags/list?n=100&last={Uri.EscapeDataString(lastTagInPage)}";
                }
                else
                {
                    nextTagsUrl = null;
                }
            }

            var tags = allTags.ToList();
            tags.Sort(StringComparer.OrdinalIgnoreCase);

            return Result<RegistryRepositoryTagsDto>.Success(new RegistryRepositoryTagsDto
            {
                Repository = repoName,
                Tags = tags,
                CanPush = canPush,
                CanDelete = canDelete
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Unable to reach upstream Docker Registry while fetching tags for {Repository}", repository);
            return Result<RegistryRepositoryTagsDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while listing tags for repository {Repository}", repository);
            return Result<RegistryRepositoryTagsDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<RegistryManifestDto>> GetManifest(string repository, string reference, int callerId)
    {
        try
        {
            var repoName = repository.Trim().Trim('/');
            var refTrimmed = reference.Trim();

            var (user, permissions, isAdmin) = await LoadCallerPermissions(callerId);
            if (user is null)
            {
                return Result<RegistryManifestDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            var (canPull, _, _) = EvaluateUserRepoPermissions(repoName, isAdmin, permissions);
            if (!canPull)
            {
                return Result<RegistryManifestDto>.Failure(RepositoryStatus.Forbidden, ResponseMessages.RegistryRepositoryAccessDenied);
            }

            using var client = CreateRegistryClient(user.Username, [
                new RegistryAccessEntryDto
                {
                    Type = "repository",
                    Name = repoName,
                    Actions = ["pull"]
                }
            ]);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/{repoName}/manifests/{Uri.EscapeDataString(refTrimmed)}");
            foreach (var accept in ManifestAcceptHeaders)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            }

            var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result<RegistryManifestDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.RegistryManifestNotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<RegistryManifestDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
            }

            var digest = response.Headers.TryGetValues("Docker-Content-Digest", out var digestValues)
                ? digestValues.FirstOrDefault() ?? string.Empty
                : string.Empty;

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var mediaType = root.TryGetProperty("mediaType", out var mtProp)
                ? mtProp.GetString() ?? response.Content.Headers.ContentType?.MediaType ?? string.Empty
                : response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            var schemaVersion = root.TryGetProperty("schemaVersion", out var svProp) && svProp.TryGetInt32(out var sv)
                ? sv
                : 2;

            var dto = new RegistryManifestDto
            {
                Repository = repoName,
                Reference = refTrimmed,
                Digest = digest,
                MediaType = mediaType,
                SchemaVersion = schemaVersion
            };

            // Multi-arch Image Index / Manifest List
            if (root.TryGetProperty("manifests", out var manifestsElement) && manifestsElement.ValueKind == JsonValueKind.Array)
            {
                dto.IsMultiArch = true;
                foreach (var m in manifestsElement.EnumerateArray())
                {
                    var pDigest = m.TryGetProperty("digest", out var dProp) ? dProp.GetString() ?? string.Empty : string.Empty;
                    var pMedia = m.TryGetProperty("mediaType", out var mProp) ? mProp.GetString() ?? string.Empty : string.Empty;
                    var pSize = m.TryGetProperty("size", out var sProp) && sProp.TryGetInt64(out var sz) ? sz : 0L;
                    var arch = string.Empty;
                    var os = string.Empty;
                    var variant = string.Empty;

                    if (m.TryGetProperty("platform", out var plat) && plat.ValueKind == JsonValueKind.Object)
                    {
                        arch = plat.TryGetProperty("architecture", out var aProp) ? aProp.GetString() ?? string.Empty : string.Empty;
                        os = plat.TryGetProperty("os", out var oProp) ? oProp.GetString() ?? string.Empty : string.Empty;
                        variant = plat.TryGetProperty("variant", out var vProp) ? vProp.GetString() ?? string.Empty : string.Empty;
                    }

                    dto.Platforms.Add(new RegistryPlatformDto
                    {
                        Digest = pDigest,
                        MediaType = pMedia,
                        Size = pSize,
                        Architecture = arch,
                        Os = os,
                        Variant = variant
                    });
                }

                dto.TotalSizeBytes = dto.Platforms.Sum(p => p.Size);
                return Result<RegistryManifestDto>.Success(dto);
            }

            // Single-arch Manifest
            if (root.TryGetProperty("layers", out var layersElement) && layersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var layer in layersElement.EnumerateArray())
                {
                    var lDigest = layer.TryGetProperty("digest", out var ldProp) ? ldProp.GetString() ?? string.Empty : string.Empty;
                    var lMedia = layer.TryGetProperty("mediaType", out var lmProp) ? lmProp.GetString() ?? string.Empty : string.Empty;
                    var lSize = layer.TryGetProperty("size", out var lsProp) && lsProp.TryGetInt64(out var lsz) ? lsz : 0L;

                    dto.Layers.Add(new RegistryLayerDto
                    {
                        Digest = lDigest,
                        MediaType = lMedia,
                        Size = lSize
                    });
                }

                dto.TotalSizeBytes = dto.Layers.Sum(l => l.Size);
            }

            // Inspect Config Blob for architecture, OS, created date, entrypoint, cmd, env, exposed ports
            if (root.TryGetProperty("config", out var configElement)
                && configElement.ValueKind == JsonValueKind.Object
                && configElement.TryGetProperty("digest", out var configDigestProp))
            {
                var configDigest = configDigestProp.GetString();
                if (!string.IsNullOrWhiteSpace(configDigest))
                {
                    await PopulateConfigDetailsAsync(client, repoName, configDigest, dto);
                }
            }

            return Result<RegistryManifestDto>.Success(dto);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Unable to reach upstream Docker Registry while fetching manifest {Repository}:{Reference}", repository, reference);
            return Result<RegistryManifestDto>.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching manifest {Repository}:{Reference}", repository, reference);
            return Result<RegistryManifestDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteManifest(string repository, string reference, int callerId, IPAddress? clientAddress)
    {
        try
        {
            var repoName = repository.Trim().Trim('/');
            var refTrimmed = reference.Trim();

            var (user, permissions, isAdmin) = await LoadCallerPermissions(callerId);
            if (user is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserNotFound);
            }

            var (_, _, canDelete) = EvaluateUserRepoPermissions(repoName, isAdmin, permissions);
            if (!canDelete)
            {
                return Result.Failure(RepositoryStatus.Forbidden, ResponseMessages.RegistryRepositoryAccessDenied);
            }

            using var client = CreateRegistryClient(user.Username, [
                new RegistryAccessEntryDto
                {
                    Type = "repository",
                    Name = repoName,
                    Actions = ["pull", "delete"]
                }
            ]);

            var digest = refTrimmed;

            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, $"/v2/{repoName}/manifests/{Uri.EscapeDataString(refTrimmed)}");
                foreach (var accept in ManifestAcceptHeaders)
                {
                    headReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
                }

                var headRes = await client.SendAsync(headReq);
                if (headRes.StatusCode == HttpStatusCode.NotFound)
                {
                    return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RegistryManifestNotFound);
                }

                if (!headRes.IsSuccessStatusCode
                    || !headRes.Headers.TryGetValues("Docker-Content-Digest", out var digestValues)
                    || string.IsNullOrWhiteSpace(digestValues.FirstOrDefault()))
                {
                    return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
                }

                digest = digestValues.First();
            }

            var deleteRes = await client.DeleteAsync($"/v2/{repoName}/manifests/{Uri.EscapeDataString(digest)}");

            if (deleteRes.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.RegistryManifestNotFound);
            }

            if (!deleteRes.IsSuccessStatusCode)
            {
                return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
            }

            context.AuditLogs.Add(new AuditLog(
                username: user.Username,
                repository: $"{repoName}:{refTrimmed}",
                actions: "delete",
                description: nameof(AuditLogEnums.Description.Manifest_Deleted),
                clientIp: clientAddress?.ToString() ?? string.Empty));

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Unable to reach upstream Docker Registry while deleting manifest {Repository}:{Reference}", repository, reference);
            return Result.Failure(RepositoryStatus.BadRequest, ResponseMessages.RegistryUpstreamError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting manifest {Repository}:{Reference}", repository, reference);
            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    private async Task PopulateConfigDetailsAsync(HttpClient client, string repoName, string configDigest, RegistryManifestDto dto)
    {
        try
        {
            var configRes = await client.GetAsync($"/v2/{repoName}/blobs/{Uri.EscapeDataString(configDigest)}");
            if (!configRes.IsSuccessStatusCode)
            {
                return;
            }

            await using var stream = await configRes.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("architecture", out var archProp))
            {
                dto.Architecture = archProp.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("os", out var osProp))
            {
                dto.Os = osProp.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("created", out var createdProp)
                && createdProp.ValueKind == JsonValueKind.String
                && DateTime.TryParse(createdProp.GetString(), out var createdAt))
            {
                dto.CreatedAt = createdAt.ToUniversalTime();
            }

            if (root.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
            {
                if (cfg.TryGetProperty("Entrypoint", out var ep) && ep.ValueKind == JsonValueKind.Array)
                {
                    dto.Entrypoint = ep.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList();
                }

                if (cfg.TryGetProperty("Cmd", out var cmd) && cmd.ValueKind == JsonValueKind.Array)
                {
                    dto.Cmd = cmd.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList();
                }

                if (cfg.TryGetProperty("Env", out var env) && env.ValueKind == JsonValueKind.Array)
                {
                    dto.Env = env.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList();
                }

                if (cfg.TryGetProperty("ExposedPorts", out var ports) && ports.ValueKind == JsonValueKind.Object)
                {
                    dto.ExposedPorts = ports.EnumerateObject().Select(p => p.Name).ToList();
                }
            }
        }
        catch
        {
            // Non-critical metadata enrichment
        }
    }

    private HttpClient CreateRegistryClient(string subject, List<RegistryAccessEntryDto> accessEntries)
    {
        var (jwt, _, _) = tokenSigner.CreateToken(subject, _options.Service, accessEntries);
        var client = httpClientFactory.CreateClient("DockerRegistry");
        client.BaseAddress = new Uri(_options.RegistryUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private async Task<(User? User, List<RepositoryPermission> Permissions, bool IsAdmin)> LoadCallerPermissions(int callerId)
    {
        var user = await context.Users
            .AsNoTracking()
            .Include(u => u.UserType)
            .FirstOrDefaultAsync(u => u.Id == callerId);

        if (user is null)
        {
            return (null, [], false);
        }

        var isAdmin = string.Equals(user.UserType?.Type, Roles.Admin, StringComparison.OrdinalIgnoreCase);
        var permissions = isAdmin
            ? []
            : await context.RepositoryPermissions
                .AsNoTracking()
                .Where(p => p.UserId == callerId)
                .ToListAsync();

        return (user, permissions, isAdmin);
    }

    private static (bool CanPull, bool CanPush, bool CanDelete) EvaluateUserRepoPermissions(
        string repositoryName,
        bool isAdmin,
        List<RepositoryPermission> permissions)
    {
        if (isAdmin)
        {
            return (true, true, true);
        }

        bool canPull = false;
        bool canPush = false;
        bool canDelete = false;

        foreach (var p in permissions)
        {
            if (RepositoryPermissionRepository.PatternMatchesRepository(p.RepositoryPattern, repositoryName))
            {
                canPull |= p.CanPull;
                canPush |= p.CanPush;
                canDelete |= p.CanDelete;
            }
        }

        return (canPull, canPush, canDelete);
    }

    internal static string? ExtractNextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var linkHeaders))
        {
            return null;
        }

        foreach (var header in linkHeaders)
        {
            var links = header.Split(',');
            foreach (var linkPart in links)
            {
                var segments = linkPart.Split(';');
                if (segments.Length < 2)
                {
                    continue;
                }

                var isNext = false;
                for (var i = 1; i < segments.Length; i++)
                {
                    var param = segments[i].Trim();
                    if (param.Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase)
                        || param.Equals("rel=next", StringComparison.OrdinalIgnoreCase))
                    {
                        isNext = true;
                        break;
                    }
                }

                if (isNext)
                {
                    var target = segments[0].Trim();
                    if (target.StartsWith('<') && target.EndsWith('>'))
                    {
                        var uriString = target[1..^1];
                        if (Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out var parsedUri))
                        {
                            return parsedUri.IsAbsoluteUri ? parsedUri.PathAndQuery : uriString;
                        }
                        return uriString;
                    }
                }
            }
        }

        return null;
    }
}
