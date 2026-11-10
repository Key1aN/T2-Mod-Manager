using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.State;

namespace T2ModManager.Core.Diagnostics;

public enum DiagnosticSource
{
    Manager,
    Game,
    GameCrash,
    ManagerState
}

public sealed record DiagnosticFile(
    string DisplayName,
    string FullPath,
    DiagnosticSource Source,
    bool IsBinary,
    long Size,
    DateTime LastWriteTime);

/// <summary>
/// Finds manager logs plus the native crash reporter outputs written next to
/// TMNT2.exe (TMNT2_Slashuur_trace.log, TMNT2_crash_*.log/.dmp, TMNT2_fatal_*.log),
/// and packs them into a single report ZIP that can be shared for diagnosis.
/// </summary>
public static class DiagnosticFiles
{
    public const long MaxViewBytes = 1024 * 1024;
    public const int MaxDumpsInReport = 5;

    public static IReadOnlyList<DiagnosticFile> Find(AppDataPaths paths, string? gameDirectory)
    {
        List<DiagnosticFile> files = [];
        AddFiles(files, paths.LogsDirectory, "manager-*.log", DiagnosticSource.Manager, binary: false);

        if (!string.IsNullOrWhiteSpace(gameDirectory) && Directory.Exists(gameDirectory))
        {
            AddFiles(files, gameDirectory, "TMNT2_crash_*.log", DiagnosticSource.GameCrash, binary: false);
            AddFiles(files, gameDirectory, "TMNT2_fatal_*.log", DiagnosticSource.GameCrash, binary: false);
            AddFiles(files, gameDirectory, "TMNT2_crash_*.dmp", DiagnosticSource.GameCrash, binary: true);
            foreach (string path in SafeEnumerate(gameDirectory, "*.log"))
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith("TMNT2_crash_", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("TMNT2_fatal_", StringComparison.OrdinalIgnoreCase))
                {
                    Add(files, path, DiagnosticSource.Game, binary: false);
                }
            }

            string managerDirectory = new GameInstallation(gameDirectory).ManagerDirectory;
            AddFiles(files, managerDirectory, "features.txt", DiagnosticSource.ManagerState, binary: false);
            AddFiles(files, managerDirectory, "transaction.json", DiagnosticSource.ManagerState, binary: false);
        }

        return files
            .GroupBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(file => file.LastWriteTime)
            .ToList();
    }

    /// <summary>Reads at most the last <see cref="MaxViewBytes"/> while the game may still hold the file open.</summary>
    public static string ReadTail(string path, long maxBytes = MaxViewBytes)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return start > 0
            ? $"[Showing the last {maxBytes / 1024} KB of {stream.Length / 1024} KB]{Environment.NewLine}{text}"
            : text;
    }

    public static async Task<string> CreateReportAsync(
        AppDataPaths paths,
        InstalledState state,
        string managerVersion,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        string reportPath = Path.Combine(
            paths.ReportsDirectory,
            $"T2-Mod-Manager-Report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        IReadOnlyList<DiagnosticFile> files = Find(paths, state.GameDirectory);
        HashSet<string> includedDumps = files
            .Where(file => file.IsBinary)
            .Take(MaxDumpsInReport)
            .Select(file => file.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string systemInfo = await BuildSystemInfoAsync(paths, state, managerVersion, files, cancellationToken)
            .ConfigureAwait(false);

        await using (FileStream output = new(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (ZipArchive zip = new(output, ZipArchiveMode.Create))
        {
            WriteText(zip, "system-info.txt", systemInfo);
            if (File.Exists(paths.StateFile))
            {
                AddEntry(zip, paths.StateFile, "manager/state.json");
            }

            foreach (DiagnosticFile file in files)
            {
                if (file.IsBinary && !includedDumps.Contains(file.FullPath))
                {
                    continue;
                }

                string folder = file.Source switch
                {
                    DiagnosticSource.Manager => "manager/logs",
                    DiagnosticSource.ManagerState => "game/.t2-mod-manager",
                    _ => "game"
                };
                AddEntry(zip, file.FullPath, $"{folder}/{file.DisplayName}");
            }
        }

        ManagerLog.Info($"Diagnostic report created: {reportPath}");
        return reportPath;
    }

    private static async Task<string> BuildSystemInfoAsync(
        AppDataPaths paths,
        InstalledState state,
        string managerVersion,
        IReadOnlyList<DiagnosticFile> files,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        builder.AppendLine("T2 Mod Manager diagnostic report");
        builder.AppendLine($"Created (local): {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Manager version: {managerVersion}");
        builder.AppendLine($"Manager folder: {AppContext.BaseDirectory}");
        builder.AppendLine($"Portable data folder: {paths.Root}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        builder.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine();
        builder.AppendLine($"Game folder: {state.GameDirectory ?? "(none selected)"}");
        builder.AppendLine($"Game build ID: {state.GameBuildId ?? "(unknown)"}");
        builder.AppendLine($"Recorded TMNT2.exe SHA-256: {state.CurrentExecutableSha256 ?? "(none)"}");
        builder.AppendLine($"Recorded TMNT.DAT SHA-256: {state.CurrentArchiveSha256 ?? "(none)"}");

        if (!string.IsNullOrWhiteSpace(state.GameDirectory))
        {
            GameInstallation installation = new(state.GameDirectory);
            if (File.Exists(installation.ExecutablePath))
            {
                // The EXE hash identifies the exact native build so the matching PDB/MAP can be used.
                string hash = await Sha256Service.HashFileAsync(installation.ExecutablePath, cancellationToken)
                    .ConfigureAwait(false);
                builder.AppendLine($"Current TMNT2.exe: {new FileInfo(installation.ExecutablePath).Length} bytes, SHA-256 {hash}");
            }
            if (File.Exists(installation.ArchivePath))
            {
                FileInfo archive = new(installation.ArchivePath);
                builder.AppendLine($"Current TMNT.DAT: {archive.Length} bytes, modified {archive.LastWriteTime:yyyy-MM-dd HH:mm:ss} (not re-hashed)");
            }
            if (File.Exists(installation.FeatureStatePath))
            {
                builder.AppendLine("features.txt:");
                builder.AppendLine(await File.ReadAllTextAsync(installation.FeatureStatePath, cancellationToken).ConfigureAwait(false));
            }
        }

        builder.AppendLine();
        builder.AppendLine("Mods:");
        if (state.Mods.Count == 0)
        {
            builder.AppendLine("  (none imported)");
        }
        foreach (InstalledMod mod in state.Mods.OrderBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  [{(mod.Active ? "x" : " ")}] {mod.Id} {mod.Version} - {mod.DisplayName}");
        }

        builder.AppendLine();
        builder.AppendLine("Collected files:");
        foreach (DiagnosticFile file in files)
        {
            builder.AppendLine($"  {file.Source,-12} {file.LastWriteTime:yyyy-MM-dd HH:mm:ss} {file.Size,12} {file.FullPath}");
        }

        return builder.ToString();
    }

    private static void AddEntry(ZipArchive zip, string sourcePath, string entryName)
    {
        try
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using Stream destination = entry.Open();
            input.CopyTo(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteText(zip, entryName + ".unreadable.txt", $"Could not read {sourcePath}: {exception.Message}");
        }
    }

    private static void WriteText(ZipArchive zip, string entryName, string text)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void AddFiles(
        List<DiagnosticFile> files,
        string directory,
        string pattern,
        DiagnosticSource source,
        bool binary)
    {
        foreach (string path in SafeEnumerate(directory, pattern))
        {
            Add(files, path, source, binary);
        }
    }

    private static void Add(List<DiagnosticFile> files, string path, DiagnosticSource source, bool binary)
    {
        try
        {
            FileInfo info = new(path);
            files.Add(new DiagnosticFile(info.Name, info.FullName, source, binary, info.Length, info.LastWriteTime));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
