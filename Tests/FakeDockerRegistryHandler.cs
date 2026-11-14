using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Tests;

public class FakeDockerRegistryHandler : HttpMessageHandler
{
    public const string DemoDigest = "sha256:46faa9a1ae6813194b53921a370f2f4f8c5e1aae228a89bceafef5847a6a3278";
    public const string ConfigDigest = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    public const string LayerDigest = "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    public const string MultiArchIndexDigest = "sha256:3333333333333333333333333333333333333333333333333333333333333333";
    public const string MultiArchArm64Digest = "sha256:4444444444444444444444444444444444444444444444444444444444444444";
    public const string Arm64ConfigDigest = "sha256:5555555555555555555555555555555555555555555555555555555555555555";
    public const string Arm64LayerDigest = "sha256:6666666666666666666666666666666666666666666666666666666666666666";
    public const string AttestationDigest = "sha256:7777777777777777777777777777777777777777777777777777777777777777";

    private readonly object _lock = new();
    private HashSet<string> _repositories = new(StringComparer.OrdinalIgnoreCase) { "team-a/demo-app", "team-b/secret-app" };
    private HashSet<string> _demoTags = new(StringComparer.OrdinalIgnoreCase) { "v1.0.0" };

    public int CatalogRequestCount { get; private set; }
    public int TagsRequestCount { get; private set; }

    public void Reset()
    {
        lock (_lock)
        {
            _repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "team-a/demo-app", "team-b/secret-app" };
            _demoTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "v1.0.0" };
            CatalogRequestCount = 0;
            TagsRequestCount = 0;
        }
    }

    public void AddRepository(string repoName)
    {
        lock (_lock)
        {
            _repositories.Add(repoName);
        }
    }

    public void AddDemoTag(string tag)
    {
        lock (_lock)
        {
            _demoTags.Add(tag);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri?.AbsolutePath ?? string.Empty);

        if (request.Method == HttpMethod.Get && path == "/v2/_catalog")
        {
            string[] repos;
            lock (_lock)
            {
                CatalogRequestCount++;
                repos = _repositories.ToArray();
            }

            var reposJson = string.Join(",", repos.Select(r => $"\"{r}\""));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "repositories": [{{reposJson}}]
                }
                """));
        }

        if (request.Method == HttpMethod.Get && path == "/v2/team-a/demo-app/tags/list")
        {
            string[] tags;
            lock (_lock)
            {
                TagsRequestCount++;
                tags = _demoTags.ToArray();
            }

            var tagsJson = string.Join(",", tags.Select(t => $"\"{t}\""));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "name": "team-a/demo-app",
                  "tags": [{{tagsJson}}]
                }
                """));
        }

        if ((request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            && (path == "/v2/team-a/demo-app/manifests/v1.0.0" || path == $"/v2/team-a/demo-app/manifests/{DemoDigest}"))
        {
            bool exists;
            lock (_lock)
            {
                exists = _demoTags.Contains("v1.0.0");
            }

            if (!exists)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var response = JsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "schemaVersion": 2,
                  "mediaType": "application/vnd.docker.distribution.manifest.v2+json",
                  "config": {
                    "mediaType": "application/vnd.docker.container.image.v1+json",
                    "size": 1024,
                    "digest": "{{ConfigDigest}}"
                  },
                  "layers": [
                    {
                      "mediaType": "application/vnd.docker.image.rootfs.diff.tar.gzip",
                      "size": 4096,
                      "digest": "{{LayerDigest}}"
                    }
                  ]
                }
                """,
                "application/vnd.docker.distribution.manifest.v2+json");

            response.Headers.TryAddWithoutValidation("Docker-Content-Digest", DemoDigest);
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Get && path == $"/v2/team-a/demo-app/blobs/{ConfigDigest}")
        {
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "architecture": "amd64",
                  "os": "linux",
                  "created": "2026-11-14T12:00:00Z"
                }
                """));
        }

        if ((request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            && (path == "/v2/team-a/demo-app/manifests/multi-arch" || path == $"/v2/team-a/demo-app/manifests/{MultiArchIndexDigest}"))
        {
            var response = JsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "schemaVersion": 2,
                  "mediaType": "application/vnd.oci.image.index.v1+json",
                  "manifests": [
                    {
                      "mediaType": "application/vnd.oci.image.manifest.v1+json",
                      "size": 512,
                      "digest": "{{AttestationDigest}}",
                      "platform": {
                        "architecture": "unknown",
                        "os": "unknown"
                      }
                    },
                    {
                      "mediaType": "application/vnd.oci.image.manifest.v1+json",
                      "size": 1024,
                      "digest": "{{MultiArchArm64Digest}}",
                      "platform": {
                        "architecture": "arm64",
                        "os": "linux",
                        "variant": "v8"
                      }
                    },
                    {
                      "mediaType": "application/vnd.oci.image.manifest.v1+json",
                      "size": 1024,
                      "digest": "{{DemoDigest}}",
                      "platform": {
                        "architecture": "amd64",
                        "os": "linux"
                      }
                    }
                  ]
                }
                """,
                "application/vnd.oci.image.index.v1+json");

            response.Headers.TryAddWithoutValidation("Docker-Content-Digest", MultiArchIndexDigest);
            return Task.FromResult(response);
        }

        if ((request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            && path == $"/v2/team-a/demo-app/manifests/{MultiArchArm64Digest}")
        {
            var response = JsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "schemaVersion": 2,
                  "mediaType": "application/vnd.oci.image.manifest.v1+json",
                  "config": {
                    "mediaType": "application/vnd.oci.image.config.v1+json",
                    "size": 1024,
                    "digest": "{{Arm64ConfigDigest}}"
                  },
                  "layers": [
                    {
                      "mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
                      "size": 8192,
                      "digest": "{{Arm64LayerDigest}}"
                    }
                  ]
                }
                """,
                "application/vnd.oci.image.manifest.v1+json");

            response.Headers.TryAddWithoutValidation("Docker-Content-Digest", MultiArchArm64Digest);
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Get && path == $"/v2/team-a/demo-app/blobs/{Arm64ConfigDigest}")
        {
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "architecture": "arm64",
                  "os": "linux",
                  "created": "2026-11-14T13:00:00Z",
                  "config": {
                    "Entrypoint": ["/app/server"],
                    "ExposedPorts": { "5090/tcp": {} }
                  }
                }
                """));
        }

        if (request.Method == HttpMethod.Delete && path == $"/v2/team-a/demo-app/manifests/{DemoDigest}")
        {
            lock (_lock)
            {
                if (!_demoTags.Remove("v1.0.0"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json, string mediaType = "application/json")
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue(mediaType))
        };
    }
}
