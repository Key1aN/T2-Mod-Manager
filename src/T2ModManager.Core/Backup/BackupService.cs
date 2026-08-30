using System.Text.Json;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;
using T2ModManager.Core.State;

namespace T2ModManager.Core.Backup;

public sealed record BackupManifest(
    int SchemaVersion,
    string BuildId,
    DateTimeOffset CreatedAtUtc,
    long ExecutableSize,
    string ExecutableSha256,
    long ArchiveSize,
    string ArchiveSha256);

public sealed record VanillaBackup(string Directory, string ExecutablePath, string ArchivePath);

public sealed class BackupService(AppDataPaths paths)
{
    public async Task<VanillaBackup> EnsureVanillaBackupAsync(
        GameInstallation installation,
        GameFingerprint fingerprint,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        string backupDirectory = Path.Combine(paths.BackupsDirectory, fingerprint.BuildId, "vanilla");
        string executableBackup = Path.Combine(backupDirectory, "TMNT2.exe");
        string archiveBackup = Path.Combine(backupDirectory, "TMNT.DAT");
        string manifestPath = Path.Combine(backupDirectory, "backup.json");
        Directory.CreateDirectory(backupDirectory);

        if (File.Exists(executableBackup) && File.Exists(archiveBackup))
        {
            progress?.Report(new OperationProgress("backup", "Verifying the existing vanilla backup…"));
            await VerifyBackupAsync(executableBackup, archiveBackup, fingerprint, cancellationToken)
                .ConfigureAwait(false);
            if (!File.Exists(manifestPath))
            {
                await WriteManifestAsync(manifestPath, fingerprint, cancellationToken).ConfigureAwait(false);
            }
            return new VanillaBackup(backupDirectory, executableBackup, archiveBackup);
        }

        if (File.Exists(executableBackup) || File.Exists(archiveBackup))
        {
            throw new InvalidDataException(
                "The vanilla backup is incomplete. Preserve it for inspection before creating a new backup.");
        }

        string temporaryExecutable = executableBackup + ".partial";
        string temporaryArchive = archiveBackup + ".partial";
        DeletePartial(temporaryExecutable);
        DeletePartial(temporaryArchive);

        progress?.Report(new OperationProgress("backup", "Backing up the original TMNT2.exe…", 0.05));
        await CopyFileAsync(installation.ExecutablePath, temporaryExecutable, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new OperationProgress("backup", "Backing up the original TMNT.DAT…", 0.15));
        await CopyFileAsync(installation.ArchivePath, temporaryArchive, cancellationToken)
            .ConfigureAwait(false);

        await VerifyBackupAsync(temporaryExecutable, temporaryArchive, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryExecutable, executableBackup);
        File.Move(temporaryArchive, archiveBackup);

        await WriteManifestAsync(manifestPath, fingerprint, cancellationToken).ConfigureAwait(false);

        progress?.Report(new OperationProgress("backup", "Vanilla backup created and verified.", 1.0));
        return new VanillaBackup(backupDirectory, executableBackup, archiveBackup);
    }

    public async Task<VanillaBackup> OpenVerifiedBackupAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        if (!string.Equals(buildId, known.BuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unknown backup build ID.");
        }

        string directory = Path.Combine(paths.BackupsDirectory, buildId, "vanilla");
        string executable = Path.Combine(directory, "TMNT2.exe");
        string archive = Path.Combine(directory, "TMNT.DAT");
        GameFingerprint expected = new(
            known.BuildId,
            known.Region,
            known.Architecture,
            known.ExecutableSize,
            known.ExecutableSha256,
            known.ArchiveSize,
            known.ArchiveSha256);
        await VerifyBackupAsync(executable, archive, expected, cancellationToken).ConfigureAwait(false);
        return new VanillaBackup(directory, executable, archive);
    }

    private static async Task VerifyBackupAsync(
        string executable,
        string archive,
        GameFingerprint expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable) || !File.Exists(archive) ||
            new FileInfo(executable).Length != expected.ExecutableSize ||
            new FileInfo(archive).Length != expected.ArchiveSize)
        {
            throw new InvalidDataException("Vanilla backup files are missing or have the wrong size.");
        }

        string executableHash = await Sha256Service.HashFileAsync(executable, cancellationToken)
            .ConfigureAwait(false);
        string archiveHash = await Sha256Service.HashFileAsync(archive, cancellationToken)
            .ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(executableHash, expected.ExecutableSha256) ||
            !Sha256Service.EqualsHash(archiveHash, expected.ArchiveSha256))
        {
            throw new InvalidDataException("Vanilla backup verification failed; it will not be used.");
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static async Task WriteManifestAsync(
        string manifestPath,
        GameFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        BackupManifest manifest = new(
            1,
            fingerprint.BuildId,
            DateTimeOffset.UtcNow,
            fingerprint.ExecutableSize,
            fingerprint.ExecutableSha256,
            fingerprint.ArchiveSize,
            fingerprint.ArchiveSha256);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestSerializer.JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static void DeletePartial(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
