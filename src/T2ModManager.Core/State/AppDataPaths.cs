namespace T2ModManager.Core.State;

public sealed class AppDataPaths
{
    public AppDataPaths(string? overrideRoot = null)
    {
        Root = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "T2-Mod-Manager");
    }

    public string Root { get; }
    public string StateFile => Path.Combine(Root, "state.json");
    public string BackupsDirectory => Path.Combine(Root, "backups");
    public string PackagesDirectory => Path.Combine(Root, "packages");
    public string CompatibilityDirectory => Path.Combine(Root, "compatibility");
    public string ObsoleteDiscCheckFixDirectory => Path.Combine(
        CompatibilityDirectory,
        "tmnt2-pc-us-na-obsolete-disc-check-fix-v1");
    public string ObsoleteDiscCheckFixExecutable => Path.Combine(
        ObsoleteDiscCheckFixDirectory,
        "TMNT2.exe");
    public string LogsDirectory => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(PackagesDirectory);
        Directory.CreateDirectory(CompatibilityDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
