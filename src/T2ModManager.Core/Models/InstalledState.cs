namespace T2ModManager.Core.Models;

public sealed class InstalledState
{
    public const int CurrentModCatalogRevision = 2;

    public int SchemaVersion { get; init; } = 1;
    public int ModCatalogRevision { get; set; }
    public string? GameDirectory { get; set; }
    public string? GameBuildId { get; set; }
    public string? CurrentExecutableSha256 { get; set; }
    public string? CurrentArchiveSha256 { get; set; }
    public string? CurrentFeatureStateSha256 { get; set; }
    public List<InstalledMod> Mods { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InstalledMod
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string? Description { get; set; }
    public required string PackagePath { get; init; }
    public bool Active { get; set; }
    public DateTimeOffset InstalledAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ModConflict(
    string FirstModId,
    string SecondModId,
    string ResourceId,
    string Reason);
