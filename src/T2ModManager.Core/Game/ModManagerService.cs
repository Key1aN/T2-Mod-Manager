using T2ModManager.Core.Archives;
using T2ModManager.Core.Backup;
using T2ModManager.Core.Conflict;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;
using T2ModManager.Core.Patching;
using T2ModManager.Core.State;
using T2ModManager.Core.Transactions;
using T2ModManager.Core.Validation;

namespace T2ModManager.Core.Game;

public sealed class ModManagerService
{
    private readonly AppDataPaths _paths;
    private readonly StateStore _stateStore;
    private readonly BackupService _backupService;

    public ModManagerService(string? appDataRoot = null)
    {
        _paths = new AppDataPaths(appDataRoot);
        _paths.EnsureCreated();
        _stateStore = new StateStore(_paths);
        _backupService = new BackupService(_paths);
    }

    public Task<InstalledState> GetStateAsync(CancellationToken cancellationToken = default) =>
        _stateStore.LoadAsync(cancellationToken);

    public async Task InitializeGameAsync(
        string gameDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(gameDirectory.Trim());
        GameInstallation installation = new(fullPath);
        await TransactionalFileInstaller.RecoverIfNeededAsync(
            installation,
            progress,
            cancellationToken).ConfigureAwait(false);
        await TransactionalFileInstaller.CleanupCompletedAsync(installation, cancellationToken)
            .ConfigureAwait(false);

        InstalledState state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state.GameDirectory is not null &&
            !Path.GetFullPath(state.GameDirectory).Equals(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "v0.1 manages one game installation. Restore or disconnect the current installation first.");
        }

        GameFingerprint fingerprint = await GameFingerprintService.InspectAsync(
            installation,
            progress,
            cancellationToken).ConfigureAwait(false);
        GameFingerprintService.RequireSupportedVanilla(fingerprint);
        _ = await _backupService.EnsureVanillaBackupAsync(
            installation,
            fingerprint,
            progress,
            cancellationToken).ConfigureAwait(false);

        state.GameDirectory = fullPath;
        state.GameBuildId = fingerprint.BuildId;
        state.CurrentExecutableSha256 = fingerprint.ExecutableSha256;
        state.CurrentArchiveSha256 = fingerprint.ArchiveSha256;
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstalledMod> InstallPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        using T2ModPackage package = T2ModPackage.Open(packagePath);
        RequireTarget(package.Manifest);

        InstalledMod? existing = state.Mods.FirstOrDefault(
            mod => mod.Id.Equals(package.Manifest.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Active)
        {
            throw new InvalidOperationException("Deactivate the installed version before replacing its package.");
        }

        string packageDirectory = Path.Combine(
            _paths.PackagesDirectory,
            package.Manifest.Id,
            package.Manifest.Version);
        Directory.CreateDirectory(packageDirectory);
        string storedPath = Path.Combine(packageDirectory, package.Manifest.Id + ".t2mod");
        string temporaryPath = storedPath + ".new";
        File.Copy(packagePath, temporaryPath, overwrite: true);
        using (T2ModPackage copiedPackage = T2ModPackage.Open(temporaryPath))
        {
            if (!string.Equals(copiedPackage.Manifest.Id, package.Manifest.Id, StringComparison.Ordinal) ||
                !string.Equals(copiedPackage.Manifest.Version, package.Manifest.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Copied package identity changed unexpectedly.");
            }
        }
        File.Move(temporaryPath, storedPath, overwrite: true);

        if (existing is not null)
        {
            state.Mods.Remove(existing);
        }

        InstalledMod installed = new()
        {
            Id = package.Manifest.Id,
            DisplayName = package.Manifest.DisplayName,
            Version = package.Manifest.Version,
            PackagePath = storedPath,
            Active = false
        };
        state.Mods.Add(installed);
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return installed;
    }

    public async Task ActivateAsync(
        string modId,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        RequireInstalledMod(state, modId);
        HashSet<string> desired = state.Mods
            .Where(mod => mod.Active)
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        desired.Add(modId);
        await ApplyDesiredStateAsync(state, desired, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(
        string modId,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        RequireInstalledMod(state, modId);
        HashSet<string> desired = state.Mods
            .Where(mod => mod.Active && !mod.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await ApplyDesiredStateAsync(state, desired, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task ImportAndEnableCompatibilityExecutableAsync(
        string executablePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        string fullPath = Path.GetFullPath(executablePath);
        KnownCompatibilityExecutable known = KnownCompatibilityExecutables.ObsoleteDiscCheckFixUsNa;

        progress?.Report(new OperationProgress(
            "verify",
            "Verifying the selected obsolete disc-check fix…",
            0.02));
        _ = PeValidator.ValidateWin32Executable(fullPath);
        await RequireFileFingerprintAsync(fullPath, known.Size, known.Sha256, cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(_paths.ObsoleteDiscCheckFixDirectory);
        string temporaryPath = _paths.ObsoleteDiscCheckFixExecutable + ".new";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        try
        {
            await CopyFileAsync(fullPath, temporaryPath, cancellationToken).ConfigureAwait(false);
            await RequireFileFingerprintAsync(temporaryPath, known.Size, known.Sha256, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _paths.ObsoleteDiscCheckFixExecutable, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        state.CompatibilityExecutable = new CompatibilityExecutableState
        {
            Id = known.Id,
            DisplayName = known.DisplayName,
            Size = known.Size,
            Sha256 = known.Sha256,
            Enabled = true
        };
        await ApplyDesiredStateAsync(
            state,
            GetActiveModIds(state),
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task EnableCompatibilityExecutableAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        CompatibilityExecutableState compatibility = RequireImportedCompatibilityExecutable(state);
        await RequireStoredCompatibilityExecutableAsync(compatibility, cancellationToken).ConfigureAwait(false);
        compatibility.Enabled = true;
        await ApplyDesiredStateAsync(
            state,
            GetActiveModIds(state),
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableCompatibilityExecutableAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        CompatibilityExecutableState compatibility = RequireImportedCompatibilityExecutable(state);
        compatibility.Enabled = false;
        await ApplyDesiredStateAsync(
            state,
            GetActiveModIds(state),
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreRetailOriginalAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.CompatibilityExecutable is not null)
        {
            state.CompatibilityExecutable.Enabled = false;
        }
        await ApplyDesiredStateAsync(
            state,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDesiredStateAsync(
        InstalledState state,
        IReadOnlySet<string> desiredModIds,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        GameInstallation installation = new(state.GameDirectory!);
        await TransactionalFileInstaller.RecoverIfNeededAsync(
            installation,
            progress,
            cancellationToken).ConfigureAwait(false);
        await TransactionalFileInstaller.CleanupCompletedAsync(installation, cancellationToken)
            .ConfigureAwait(false);
        await RequireCurrentStateUntamperedAsync(state, installation, progress, cancellationToken)
            .ConfigureAwait(false);
        bool archiveStateChanged = DoesArchiveStateChange(state, desiredModIds);

        VanillaBackup backup = await _backupService.OpenVerifiedBackupAsync(
            state.GameBuildId!,
            cancellationToken).ConfigureAwait(false);

        List<T2ModPackage> packages = [];
        string stageDirectory = Path.Combine(installation.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageDirectory);
        try
        {
            foreach (InstalledMod installed in state.Mods.Where(mod => desiredModIds.Contains(mod.Id)))
            {
                T2ModPackage package = T2ModPackage.Open(installed.PackagePath);
                RequireTarget(package.Manifest);
                packages.Add(package);
            }

            ValidateDesiredSet(packages.Select(package => package.Manifest).ToArray());
            long requiredSpace = archiveStateChanged
                ? KnownGameBuilds.UsNaRetail.ArchiveSize + 128L * 1024L * 1024L
                : 64L * 1024L * 1024L;
            RequireFreeSpace(installation, requiredSpace);

            string stagedExecutable = Path.Combine(stageDirectory, "TMNT2.exe");
            string stagedArchive = Path.Combine(stageDirectory, "TMNT.DAT");
            string? compatibilityExecutable = null;
            if (state.CompatibilityExecutable?.Enabled == true)
            {
                await RequireStoredCompatibilityExecutableAsync(
                    state.CompatibilityExecutable,
                    cancellationToken).ConfigureAwait(false);
                compatibilityExecutable = _paths.ObsoleteDiscCheckFixExecutable;
            }
            await BuildExecutableAsync(
                backup.ExecutablePath,
                compatibilityExecutable,
                stagedExecutable,
                packages,
                stageDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (archiveStateChanged)
            {
                await BuildArchiveAsync(
                    backup.ArchivePath,
                    stagedArchive,
                    packages,
                    stageDirectory,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new OperationProgress("validate", "Validating staged game files…", 0.65));
            _ = PeValidator.ValidateWin32Executable(stagedExecutable);
            string executableHash = await Sha256Service.HashFileAsync(stagedExecutable, cancellationToken)
                .ConfigureAwait(false);
            string archiveHash;
            if (archiveStateChanged)
            {
                _ = AfsArchive.Read(stagedArchive);
                archiveHash = await Sha256Service.HashFileAsync(stagedArchive, cancellationToken)
                    .ConfigureAwait(false);
                await TransactionalFileInstaller.CommitAsync(
                    installation,
                    stagedExecutable,
                    stagedArchive,
                    executableHash,
                    archiveHash,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                archiveHash = state.CurrentArchiveSha256!;
                await TransactionalFileInstaller.CommitExecutableAsync(
                    installation,
                    stagedExecutable,
                    executableHash,
                    archiveHash,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (InstalledMod mod in state.Mods)
            {
                mod.Active = desiredModIds.Contains(mod.Id);
            }
            state.CurrentExecutableSha256 = executableHash;
            state.CurrentArchiveSha256 = archiveHash;
            await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
            await TransactionalFileInstaller.CleanupCompletedAsync(installation, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (T2ModPackage package in packages)
            {
                package.Dispose();
            }

            if (Directory.Exists(stageDirectory))
            {
                Directory.Delete(stageDirectory, recursive: true);
            }
        }
    }

    private static async Task BuildExecutableAsync(
        string vanillaExecutable,
        string? compatibilityExecutable,
        string stagedExecutable,
        IReadOnlyList<T2ModPackage> packages,
        string stageDirectory,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<(T2ModPackage Package, ModOperation Operation)> operations = packages
            .SelectMany(package => package.Manifest.Operations
                .Where(operation => operation.Type == "apply-t2delta")
                .Select(operation => (package, operation)))
            .ToList();

        if (operations.Count == 0)
        {
            string sourceExecutable = compatibilityExecutable ?? vanillaExecutable;
            string message = compatibilityExecutable is null
                ? "Staging the verified retail executable…"
                : "Staging the obsolete disc-check compatibility executable…";
            progress?.Report(new OperationProgress("stage", message, 0.1));
            await CopyFileAsync(sourceExecutable, stagedExecutable, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (operations.Count != 1)
        {
            throw new InvalidDataException("v0.1 accepts one resolved T2 Core EXE delta at a time.");
        }

        (T2ModPackage package, ModOperation operation) = operations[0];
        await RequireFileFingerprintAsync(
            vanillaExecutable,
            operation.ExpectedInputSize!.Value,
            operation.ExpectedInputSha256!,
            cancellationToken).ConfigureAwait(false);
        string deltaPath = Path.Combine(stageDirectory, "core.t2delta");
        await package.ExtractVerifiedPayloadAsync(operation, deltaPath, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new OperationProgress("stage", "Building the selected T2 Core executable…", 0.2));
        await T2Delta.ApplyAsync(
            vanillaExecutable,
            deltaPath,
            stagedExecutable,
            cancellationToken).ConfigureAwait(false);
        await RequireFileFingerprintAsync(
            stagedExecutable,
            operation.ExpectedOutputSize!.Value,
            operation.ExpectedOutputSha256!,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task BuildArchiveAsync(
        string vanillaArchive,
        string stagedArchive,
        IReadOnlyList<T2ModPackage> packages,
        string stageDirectory,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<(T2ModPackage Package, ModOperation Operation)> operations = packages
            .SelectMany(package => package.Manifest.Operations
                .Where(operation => operation.Type == "replace-afs-entry")
                .Select(operation => (package, operation)))
            .ToList();

        if (operations.Count == 0)
        {
            progress?.Report(new OperationProgress("stage", "Staging the vanilla archive…", 0.35));
            await CopyFileAsync(vanillaArchive, stagedArchive, cancellationToken).ConfigureAwait(false);
            return;
        }

        AfsArchiveInfo sourceInfo = AfsArchive.Read(vanillaArchive);
        Dictionary<int, string> replacements = [];
        foreach ((T2ModPackage package, ModOperation operation) in operations)
        {
            int entryId = operation.EntryId!.Value;
            if (!replacements.TryAdd(entryId, string.Empty))
            {
                throw new InvalidDataException($"More than one mod replaces AFS entry {entryId}.");
            }

            AfsEntry entry = sourceInfo.Entries[entryId];
            if (entry.Size != operation.ExpectedInputSize!.Value)
            {
                throw new InvalidDataException($"Original AFS entry {entryId} has the wrong size.");
            }

            string originalHash = await Sha256Service.HashRangeAsync(
                vanillaArchive,
                entry.Offset,
                entry.Size,
                cancellationToken).ConfigureAwait(false);
            if (!Sha256Service.EqualsHash(originalHash, operation.ExpectedInputSha256!))
            {
                throw new InvalidDataException($"Original AFS entry {entryId} hash mismatch.");
            }

            string payloadPath = Path.Combine(stageDirectory, $"afs-entry-{entryId}.lpac");
            await package.ExtractVerifiedPayloadAsync(operation, payloadPath, cancellationToken)
                .ConfigureAwait(false);
            _ = LpacValidator.Validate(payloadPath);
            await RequireFileFingerprintAsync(
                payloadPath,
                operation.ExpectedOutputSize!.Value,
                operation.ExpectedOutputSha256!,
                cancellationToken).ConfigureAwait(false);
            replacements[entryId] = payloadPath;
        }

        progress?.Report(new OperationProgress("stage", "Rebuilding TMNT.DAT from the vanilla backup…", 0.4));
        Progress<double> archiveProgress = new(fraction =>
            progress?.Report(new OperationProgress(
                "stage",
                "Rebuilding TMNT.DAT from the vanilla backup…",
                0.4 + fraction * 0.2)));
        _ = await AfsArchive.RebuildAsync(
            vanillaArchive,
            stagedArchive,
            replacements,
            archiveProgress,
            cancellationToken).ConfigureAwait(false);

        AfsArchiveInfo outputInfo = AfsArchive.Read(stagedArchive);
        foreach ((_, ModOperation operation) in operations)
        {
            AfsEntry outputEntry = outputInfo.Entries[operation.EntryId!.Value];
            string outputHash = await Sha256Service.HashRangeAsync(
                stagedArchive,
                outputEntry.Offset,
                outputEntry.Size,
                cancellationToken).ConfigureAwait(false);
            if (outputEntry.Size != operation.ExpectedOutputSize!.Value ||
                !Sha256Service.EqualsHash(outputHash, operation.ExpectedOutputSha256!))
            {
                throw new InvalidDataException($"Staged AFS entry {operation.EntryId} verification failed.");
            }
        }
    }

    private static void ValidateDesiredSet(IReadOnlyList<ModManifest> manifests)
    {
        IReadOnlyList<ModConflict> conflicts = ConflictDetector.FindConflicts(manifests);
        if (conflicts.Count > 0)
        {
            string details = string.Join(
                Environment.NewLine,
                conflicts.Select(conflict =>
                    $"{conflict.FirstModId} ↔ {conflict.SecondModId}: {conflict.ResourceId}"));
            throw new InvalidOperationException("The selected mods conflict:" + Environment.NewLine + details);
        }

        HashSet<string> active = manifests.Select(manifest => manifest.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ModManifest manifest in manifests)
        {
            ModDependency[] missing = manifest.Dependencies
                .Where(dependency => !active.Contains(dependency.Id))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{manifest.DisplayName} requires: {string.Join(", ", missing.Select(item => item.Id))}");
            }
        }
    }

    private static bool DoesArchiveStateChange(
        InstalledState state,
        IReadOnlySet<string> desiredModIds)
    {
        foreach (InstalledMod mod in state.Mods)
        {
            bool wasActive = mod.Active;
            bool willBeActive = desiredModIds.Contains(mod.Id);
            if (wasActive == willBeActive)
            {
                continue;
            }

            using T2ModPackage package = T2ModPackage.Open(mod.PackagePath);
            RequireTarget(package.Manifest);
            if (package.Manifest.Operations.Any(operation => operation.Type == "replace-afs-entry"))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireTarget(ModManifest manifest)
    {
        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        if (!Sha256Service.EqualsHash(manifest.Target.ExecutableSha256, known.ExecutableSha256) ||
            !Sha256Service.EqualsHash(manifest.Target.ArchiveSha256, known.ArchiveSha256))
        {
            throw new InvalidDataException("Package does not target the verified US/NA retail baseline.");
        }
    }

    private static InstalledMod RequireInstalledMod(InstalledState state, string modId) =>
        state.Mods.FirstOrDefault(mod => mod.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Mod is not installed: {modId}");

    private static HashSet<string> GetActiveModIds(InstalledState state) =>
        state.Mods
            .Where(mod => mod.Active)
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static CompatibilityExecutableState RequireImportedCompatibilityExecutable(
        InstalledState state) =>
        state.CompatibilityExecutable
        ?? throw new InvalidOperationException("Import the supported obsolete disc-check fix EXE first.");

    private async Task RequireStoredCompatibilityExecutableAsync(
        CompatibilityExecutableState compatibility,
        CancellationToken cancellationToken)
    {
        KnownCompatibilityExecutable known = KnownCompatibilityExecutables.ObsoleteDiscCheckFixUsNa;
        if (!string.Equals(compatibility.Id, known.Id, StringComparison.Ordinal) ||
            compatibility.Size != known.Size ||
            !Sha256Service.EqualsHash(compatibility.Sha256, known.Sha256))
        {
            throw new InvalidDataException("The stored compatibility executable metadata is unsupported.");
        }

        _ = PeValidator.ValidateWin32Executable(_paths.ObsoleteDiscCheckFixExecutable);
        await RequireFileFingerprintAsync(
            _paths.ObsoleteDiscCheckFixExecutable,
            known.Size,
            known.Sha256,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<InstalledState> RequireInitializedStateAsync(CancellationToken cancellationToken)
    {
        InstalledState state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(state.GameDirectory) ||
            string.IsNullOrWhiteSpace(state.GameBuildId) ||
            !Sha256Service.IsSha256(state.CurrentExecutableSha256) ||
            !Sha256Service.IsSha256(state.CurrentArchiveSha256))
        {
            throw new InvalidOperationException("Select and verify the game folder first, or repair its manager state.");
        }

        return state;
    }

    private static async Task RequireCurrentStateUntamperedAsync(
        InstalledState state,
        GameInstallation installation,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new OperationProgress("verify", "Checking the currently managed game files…"));
        string executableHash = await Sha256Service.HashFileAsync(
            installation.ExecutablePath,
            cancellationToken).ConfigureAwait(false);
        string archiveHash = await Sha256Service.HashFileAsync(
            installation.ArchivePath,
            cancellationToken).ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(executableHash, state.CurrentExecutableSha256 ?? string.Empty) ||
            !Sha256Service.EqualsHash(archiveHash, state.CurrentArchiveSha256 ?? string.Empty))
        {
            throw new InvalidDataException(
                "The managed TMNT2.exe or TMNT.DAT changed outside the manager. " +
                "Use Verify/Repair instead of applying another mod state.");
        }
    }

    private static async Task RequireFileFingerprintAsync(
        string path,
        long expectedSize,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != expectedSize)
        {
            throw new InvalidDataException($"File size mismatch: {Path.GetFileName(path)}");
        }

        string hash = await Sha256Service.HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(hash, expectedHash))
        {
            throw new InvalidDataException($"File hash mismatch: {Path.GetFileName(path)}");
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

    private static void RequireFreeSpace(GameInstallation installation, long requiredBytes)
    {
        string root = Path.GetPathRoot(installation.RootDirectory)
            ?? throw new InvalidOperationException("Unable to determine the game drive.");
        DriveInfo drive = new(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"At least {requiredBytes / (1024 * 1024)} MiB of free space is required on {root}.");
        }
    }
}
