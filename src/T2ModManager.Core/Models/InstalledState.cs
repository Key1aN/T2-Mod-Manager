namespace T2ModManager.Core.Models;

public sealed class InstalledState
{
    public int SchemaVersion { get; init; } = 1;
    public string? GameDirectory { get; set; }
    public string? GameBuildId { get; set; }
    public string? CurrentExecutableSha256 { get; set; }
    public string? CurrentArchiveSha256 { get; set; }
    public CompatibilityExecutableState? CompatibilityExecutable { get; set; }
    public List<InstalledMod> Mods { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CompatibilityExecutableState
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public bool Enabled { get; set; }
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class InstalledMod
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string PackagePath { get; init; }
    public bool Active { get; set; }
    public DateTimeOffset InstalledAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ModConflict(
    string FirstModId,
    string SecondModId,
    string ResourceId,
    string Reason);
