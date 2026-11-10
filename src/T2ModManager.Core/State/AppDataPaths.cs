namespace T2ModManager.Core.State;

/// <summary>
/// Portable manager data layout. Since v0.2.3 every manager file lives beside the
/// manager executable in <see cref="PortableFolderName"/>; nothing is written to
/// the Windows user profile.
/// </summary>
public sealed class AppDataPaths
{
    public const string PortableFolderName = "T2-Mod-Manager-Data";
    public const string LegacyFolderName = "T2-Mod-Manager";

    public AppDataPaths(string? overrideRoot = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(overrideRoot ?? DefaultPortableRoot()));
    }

    public string Root { get; }
    public string StateFile => Path.Combine(Root, "state.json");
    public string BackupsDirectory => Path.Combine(Root, "backups");
    public string PackagesDirectory => Path.Combine(Root, "packages");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ReportsDirectory => Path.Combine(Root, "reports");

    /// <summary>The folder next to the running manager executable.</summary>
    public static string DefaultPortableRoot() =>
        Path.Combine(AppContext.BaseDirectory, PortableFolderName);

    /// <summary>The per-user location used by v0.1 through v0.2.2. Read only for migration.</summary>
    public static string LegacyLocalAppDataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyFolderName);

    public void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(PackagesDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(ReportsDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException(
                "T2 Mod Manager is portable and keeps its data beside its EXE in " +
                $"\"{Root}\", but that folder cannot be written. Move the manager folder to a " +
                "writable location (for example not Program Files) and start it again.",
                exception);
        }
    }

    /// <summary>Converts a path inside <see cref="Root"/> to a portable relative path.</summary>
    public string ToStoredPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(Root, full);
        bool inside = !Path.IsPathFullyQualified(relative) &&
                      relative != ".." &&
                      !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                      !relative.StartsWith("../", StringComparison.Ordinal);
        return inside ? relative : full;
    }

    /// <summary>Resolves a stored relative path against <see cref="Root"/>.</summary>
    public string ResolveStoredPath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || Path.IsPathFullyQualified(storedPath))
        {
            return storedPath;
        }

        return Path.GetFullPath(Path.Combine(Root, storedPath));
    }
}
