using System.Text.Json.Serialization;

namespace T2ModManager.Core.Models;

public sealed class ModManifest
{
    public int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public required GameTarget Target { get; init; }
    public List<ResourceClaim> Resources { get; init; } = [];
    public List<ModDependency> Dependencies { get; init; } = [];
    public List<string> Conflicts { get; init; } = [];
    public List<ModOperation> Operations { get; init; } = [];
}

public sealed class GameTarget
{
    public required string Game { get; init; }
    public required string Region { get; init; }
    public required string Architecture { get; init; }
    public required string ExecutableSha256 { get; init; }
    public required string ArchiveSha256 { get; init; }
}

public sealed class ResourceClaim
{
    public required string Id { get; init; }
    public string Mode { get; init; } = "exclusive";
    public string? Description { get; init; }
}

public sealed class ModDependency
{
    public required string Id { get; init; }
    public required string VersionRange { get; init; }
}

public sealed class ModOperation
{
    public required string Type { get; init; }
    public required string Target { get; init; }
    public required string Payload { get; init; }
    public required string PayloadSha256 { get; init; }
    public long PayloadSize { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EntryId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogicalPath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedInputSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedInputSize { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedOutputSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpectedOutputSize { get; init; }
}

