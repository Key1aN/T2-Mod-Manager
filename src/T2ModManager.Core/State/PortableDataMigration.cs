using T2ModManager.Core.Diagnostics;
using T2ModManager.Core.Models;

namespace T2ModManager.Core.State;

public enum PortableMigrationOutcome
{
    NotNeeded,
    Migrated
}

public sealed record PortableMigrationResult(
    PortableMigrationOutcome Outcome,
    string? LegacyRoot,
    int CopiedFiles);

/// <summary>
/// One-time copy of v0.1–v0.2.2 per-user data (%LOCALAPPDATA%\T2-Mod-Manager) into
/// the portable data folder. The legacy folder is only read, never modified or
/// deleted. The copied vanilla backup is still hash-verified before every use.
/// </summary>
public static class PortableDataMigration
{
    public const string MarkerFileName = "migrated-from-localappdata.txt";

    public static async Task<PortableMigrationResult> MigrateIfNeededAsync(
        AppDataPaths paths,
        string legacyRoot,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string legacyFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRoot));
        if (string.Equals(legacyFull, paths.Root, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(paths.StateFile))
        {
            return new PortableMigrationResult(PortableMigrationOutcome.NotNeeded, null, 0);
        }

        AppDataPaths legacy = new(legacyFull);
        if (!File.Exists(legacy.StateFile))
        {
            return new PortableMigrationResult(PortableMigrationOutcome.NotNeeded, null, 0);
        }

        ManagerLog.Info($"Migrating legacy per-user data from \"{legacyFull}\" to portable folder \"{paths.Root}\".");
        paths.EnsureCreated();

        int copied = 0;
        copied += await CopyTreeAsync(legacy.BackupsDirectory, paths.BackupsDirectory, "vanilla backup", progress, cancellationToken)
            .ConfigureAwait(false);
        copied += await CopyTreeAsync(legacy.PackagesDirectory, paths.PackagesDirectory, "mod packages", progress, cancellationToken)
            .ConfigureAwait(false);

        // The state file is written last: its presence marks a completed migration,
        // so an interrupted copy is simply resumed on the next start.
        InstalledState state = await new StateStore(legacy).LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (InstalledMod mod in state.Mods)
        {
            string relative = legacy.ToStoredPath(mod.PackagePath);
            mod.PackagePath = Path.IsPathFullyQualified(relative)
                ? relative
                : Path.Combine(paths.Root, relative);
        }

        await new StateStore(paths).SaveAsync(state, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(paths.Root, MarkerFileName),
            $"Copied from: {legacyFull}{Environment.NewLine}" +
            $"Copied at (UTC): {DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
            $"Files copied: {copied}{Environment.NewLine}" +
            "The old folder is no longer used by T2 Mod Manager and can be deleted.",
            cancellationToken).ConfigureAwait(false);

        ManagerLog.Info($"Legacy data migration completed ({copied} files copied). The old folder was left untouched.");
        progress?.Report(new OperationProgress("migrate", "Previous manager data copied into the portable folder.", 1.0));
        return new PortableMigrationResult(PortableMigrationOutcome.Migrated, legacyFull, copied);
    }

    private static async Task<int> CopyTreeAsync(
        string sourceRoot,
        string destinationRoot,
        string label,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return 0;
        }

        int copied = 0;
        foreach (string source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (source.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(".new", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, source));
            long length = new FileInfo(source).Length;
            if (File.Exists(destination) && new FileInfo(destination).Length == length)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            progress?.Report(new OperationProgress(
                "migrate",
                $"Copying previous {label} into the portable folder: {Path.GetFileName(source)}…"));
            string partial = destination + ".partial";
            await using (FileStream input = new(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            if (new FileInfo(partial).Length != length)
            {
                throw new IOException($"Copy of {source} was incomplete; migration stopped safely.");
            }

            File.Move(partial, destination, overwrite: true);
            copied++;
        }

        return copied;
    }
}
