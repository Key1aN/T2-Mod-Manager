using System.Diagnostics;
using System.Text.Json;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;

namespace T2ModManager.Core.Transactions;

public static class TransactionalFileInstaller
{
    public static async Task CommitAsync(
        GameInstallation installation,
        string stagedExecutable,
        string stagedArchive,
        string expectedExecutableHash,
        string expectedArchiveHash,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stagedExecutable) || !File.Exists(stagedArchive))
        {
            throw new FileNotFoundException("Both staged game files must exist before commit.");
        }

        await CommitCoreAsync(
            installation,
            stagedExecutable,
            stagedArchive,
            expectedExecutableHash,
            expectedArchiveHash,
            includesArchive: true,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task CommitExecutableAsync(
        GameInstallation installation,
        string stagedExecutable,
        string expectedExecutableHash,
        string expectedArchiveHash,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stagedExecutable))
        {
            throw new FileNotFoundException("The staged executable must exist before commit.");
        }

        await CommitCoreAsync(
            installation,
            stagedExecutable,
            stagedArchive: string.Empty,
            expectedExecutableHash,
            expectedArchiveHash,
            includesArchive: false,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task CommitCoreAsync(
        GameInstallation installation,
        string stagedExecutable,
        string stagedArchive,
        string expectedExecutableHash,
        string expectedArchiveHash,
        bool includesArchive,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        RequireGameStopped();

        Directory.CreateDirectory(installation.ManagerDirectory);
        Directory.CreateDirectory(installation.RollbackDirectory);
        string transactionId = Guid.NewGuid().ToString("N");
        string rollbackExecutable = Path.Combine(
            installation.RollbackDirectory,
            $"{transactionId}-TMNT2.exe");
        string rollbackArchive = Path.Combine(
            installation.RollbackDirectory,
            $"{transactionId}-TMNT.DAT");

        TransactionJournal journal = new()
        {
            TransactionId = transactionId,
            GameDirectory = installation.RootDirectory,
            StagedExecutable = stagedExecutable,
            StagedArchive = stagedArchive,
            RollbackExecutable = rollbackExecutable,
            RollbackArchive = rollbackArchive,
            ExpectedExecutableSha256 = expectedExecutableHash,
            ExpectedArchiveSha256 = expectedArchiveHash,
            IncludesArchive = includesArchive,
            Phase = TransactionPhase.Prepared
        };
        await SaveJournalAsync(installation.JournalPath, journal, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            progress?.Report(new OperationProgress("commit", "Installing TMNT2.exe…", 0.2));
            File.Replace(stagedExecutable, installation.ExecutablePath, rollbackExecutable, ignoreMetadataErrors: true);
            journal.Phase = TransactionPhase.ExeCommitted;
            await SaveJournalAsync(installation.JournalPath, journal, cancellationToken)
                .ConfigureAwait(false);

            if (includesArchive)
            {
                progress?.Report(new OperationProgress("commit", "Installing TMNT.DAT…", 0.55));
                File.Replace(stagedArchive, installation.ArchivePath, rollbackArchive, ignoreMetadataErrors: true);
                journal.Phase = TransactionPhase.DatCommitted;
                await SaveJournalAsync(installation.JournalPath, journal, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(new OperationProgress("commit", "Verifying the installed files…", 0.75));
            string executableHash = await Sha256Service.HashFileAsync(
                installation.ExecutablePath,
                cancellationToken).ConfigureAwait(false);
            string archiveHash = await Sha256Service.HashFileAsync(
                installation.ArchivePath,
                cancellationToken).ConfigureAwait(false);
            if (!Sha256Service.EqualsHash(executableHash, expectedExecutableHash) ||
                !Sha256Service.EqualsHash(archiveHash, expectedArchiveHash))
            {
                throw new InvalidDataException("Installed file verification failed; rollback is required.");
            }

            journal.Phase = TransactionPhase.Completed;
            await SaveJournalAsync(installation.JournalPath, journal, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new OperationProgress("commit", "Installation committed safely.", 1.0));
        }
        catch
        {
            await RollbackAsync(installation, journal, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public static async Task RecoverIfNeededAsync(
        GameInstallation installation,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(installation.JournalPath))
        {
            return;
        }

        TransactionJournal journal = await LoadJournalAsync(
            installation.JournalPath,
            cancellationToken).ConfigureAwait(false);
        if (!Path.GetFullPath(journal.GameDirectory)
            .Equals(Path.GetFullPath(installation.RootDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Transaction journal belongs to a different game directory.");
        }

        if (journal.Phase == TransactionPhase.Completed)
        {
            return;
        }

        if (journal.Phase == TransactionPhase.RolledBack)
        {
            DeleteIfExists(journal.StagedExecutable);
            if (journal.IncludesArchive)
            {
                DeleteIfExists(journal.StagedArchive);
            }
            File.Delete(installation.JournalPath);
            return;
        }

        progress?.Report(new OperationProgress("recovery", "Recovering an interrupted installation…"));
        RequireGameStopped();
        await RollbackAsync(installation, journal, cancellationToken).ConfigureAwait(false);
        progress?.Report(new OperationProgress("recovery", "Interrupted installation was rolled back.", 1.0));
    }

    public static async Task CleanupCompletedAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(installation.JournalPath))
        {
            return;
        }

        TransactionJournal journal = await LoadJournalAsync(
            installation.JournalPath,
            cancellationToken).ConfigureAwait(false);
        if (journal.Phase != TransactionPhase.Completed)
        {
            return;
        }

        DeleteIfExists(journal.RollbackExecutable);
        if (journal.IncludesArchive)
        {
            DeleteIfExists(journal.RollbackArchive);
        }
        DeleteIfExists(journal.StagedExecutable);
        if (journal.IncludesArchive)
        {
            DeleteIfExists(journal.StagedArchive);
        }
        File.Delete(installation.JournalPath);
    }

    private static async Task RollbackAsync(
        GameInstallation installation,
        TransactionJournal journal,
        CancellationToken cancellationToken)
    {
        journal.Phase = TransactionPhase.RollingBack;
        await SaveJournalAsync(installation.JournalPath, journal, cancellationToken).ConfigureAwait(false);

        if (journal.IncludesArchive && File.Exists(journal.RollbackArchive))
        {
            File.Replace(journal.RollbackArchive, installation.ArchivePath, null, ignoreMetadataErrors: true);
        }

        if (File.Exists(journal.RollbackExecutable))
        {
            File.Replace(journal.RollbackExecutable, installation.ExecutablePath, null, ignoreMetadataErrors: true);
        }

        journal.Phase = TransactionPhase.RolledBack;
        await SaveJournalAsync(installation.JournalPath, journal, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireGameStopped()
    {
        if (Process.GetProcessesByName("TMNT2").Length > 0)
        {
            throw new InvalidOperationException("Close TMNT2 before changing its files.");
        }
    }

    private static async Task SaveJournalAsync(
        string path,
        TransactionJournal journal,
        CancellationToken cancellationToken)
    {
        journal.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Journal has no parent directory."));
        string temporary = path + ".new";
        await using (FileStream stream = new(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                journal,
                ManifestSerializer.JsonOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<TransactionJournal> LoadJournalAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        TransactionJournal journal = await JsonSerializer.DeserializeAsync<TransactionJournal>(
            stream,
            ManifestSerializer.JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Transaction journal is empty.");
        if (journal.SchemaVersion != 1)
        {
            throw new InvalidDataException("Unsupported transaction journal version.");
        }

        return journal;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
