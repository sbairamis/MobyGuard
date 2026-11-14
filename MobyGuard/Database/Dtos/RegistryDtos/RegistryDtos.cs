namespace MobyGuard.Database.Dtos.RegistryDtos;

public class RegistryRepositoryDto
{
    public string Name { get; set; } = string.Empty;
    public bool CanPull { get; set; }
    public bool CanPush { get; set; }
    public bool CanDelete { get; set; }
}

public class RegistryRepositoryTagsDto
{
    public string Repository { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool CanPush { get; set; }
    public bool CanDelete { get; set; }
}

public class RegistryManifestDto
{
    public string Repository { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public bool IsMultiArch { get; set; }
    public long TotalSizeBytes { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public List<string> Entrypoint { get; set; } = [];
    public List<string> Cmd { get; set; } = [];
    public List<string> Env { get; set; } = [];
    public List<string> ExposedPorts { get; set; } = [];
    public List<RegistryLayerDto> Layers { get; set; } = [];
    public List<RegistryPlatformDto> Platforms { get; set; } = [];
}

public class RegistryLayerDto
{
    public string Digest { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class RegistryPlatformDto
{
    public string Digest { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
}

public class RegistryEventEnvelopeDto
{
    public List<RegistryEventItemDto> Events { get; set; } = [];
}

public class RegistryEventItemDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public RegistryEventTargetDto? Target { get; set; }
    public RegistryEventRequestDto? Request { get; set; }
    public RegistryEventActorDto? Actor { get; set; }
}

public class RegistryEventTargetDto
{
    public string MediaType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Digest { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class RegistryEventRequestDto
{
    public string Id { get; set; } = string.Empty;
    public string Addr { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public class RegistryEventActorDto
{
    public string Name { get; set; } = string.Empty;
}

public class RegistryWebhookResultDto
{
    public int ProcessedEvents { get; set; }
}
