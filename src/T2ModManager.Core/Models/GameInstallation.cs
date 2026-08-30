namespace T2ModManager.Core.Models;

public sealed record GameInstallation(string RootDirectory)
{
    public string ExecutablePath => Path.Combine(RootDirectory, "TMNT2.exe");
    public string ArchivePath => Path.Combine(RootDirectory, "TMNT.DAT");
    public string ManagerDirectory => Path.Combine(RootDirectory, ".t2-mod-manager");
    public string StagingDirectory => Path.Combine(ManagerDirectory, "staging");
    public string RollbackDirectory => Path.Combine(ManagerDirectory, "rollback");
    public string JournalPath => Path.Combine(ManagerDirectory, "transaction.json");
}

public sealed record GameFingerprint(
    string BuildId,
    string Region,
    string Architecture,
    long ExecutableSize,
    string ExecutableSha256,
    long ArchiveSize,
    string ArchiveSha256);

public sealed record KnownGameBuild(
    string BuildId,
    string Region,
    string Architecture,
    long ExecutableSize,
    string ExecutableSha256,
    long ArchiveSize,
    string ArchiveSha256);

public static class KnownGameBuilds
{
    public static readonly KnownGameBuild UsNaRetail = new(
        BuildId: "tmnt2-pc-us-na-retail-2004-08-19",
        Region: "US/NA",
        Architecture: "x86",
        ExecutableSize: 4_796_416,
        ExecutableSha256: "b827478461e1687f9404679211fc2d2d47bd7222aeb81a8554bd793575a89564",
        ArchiveSize: 502_067_200,
        ArchiveSha256: "cadae3cea3ad60a1f61acf4984f47adb15efccc880fe82c254b332196caa04b2");
}

public sealed record KnownCompatibilityExecutable(
    string Id,
    string DisplayName,
    long Size,
    string Sha256);

public static class KnownCompatibilityExecutables
{
    public static readonly KnownCompatibilityExecutable ObsoleteDiscCheckFixUsNa = new(
        Id: "tmnt2-pc-us-na-obsolete-disc-check-fix-v1",
        DisplayName: "Fix Obsolete Disc Check",
        Size: 4_796_416,
        Sha256: "86d508d0ba5905cb457577a3a43c3e397de5e835e2a221c97c3caca7132069ab");
}
