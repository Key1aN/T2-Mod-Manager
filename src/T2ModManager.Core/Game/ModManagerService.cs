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

    /// <summary>The portable data folder used by this manager instance.</summary>
    public AppDataPaths Paths => _paths;

    /// <summary>Copies v0.2.2-and-earlier per-user data into the portable folder once.</summary>
    public Task<PortableMigrationResult> MigrateLegacyDataAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        PortableDataMigration.MigrateIfNeededAsync(
            _paths,
            AppDataPaths.LegacyLocalAppDataRoot(),
            progress,
            cancellationToken);

    public async Task<InstalledState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        InstalledState state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        bool preserveActiveRecovery =
            state.Mods.Any(mod => mod.Active) &&
            GameDirectoryLocator.ContainsRequiredFiles(state.GameDirectory);
        if (StateMigration.PrepareModCatalog(state, preserveActiveRecovery))
        {
            await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }

        foreach (InstalledMod mod in state.Mods.Where(mod => string.IsNullOrWhiteSpace(mod.Description)))
        {
            mod.Description = TryReadInstalledDescription(mod);
        }

        return state;
    }

    public async Task InitializeGameAsync(
        string gameDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = GameDirectoryLocator.Normalize(gameDirectory);
        GameInstallation installation = new(fullPath);
        await TransactionalFileInstaller.RecoverIfNeededAsync(
            installation,
            progress,
            cancellationToken).ConfigureAwait(false);
        await TransactionalFileInstaller.CleanupCompletedAsync(installation, cancellationToken)
            .ConfigureAwait(false);

        InstalledState state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state.GameDirectory is not null &&
            !GameDirectoryLocator.PathsEqual(state.GameDirectory, fullPath) &&
            state.Mods.Any(mod => mod.Active))
        {
            throw new InvalidOperationException(
                "Deactivate all active mods before selecting a different game installation.");
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
        state.CurrentFeatureStateSha256 = File.Exists(installation.FeatureStatePath)
            ? await Sha256Service.HashFileAsync(installation.FeatureStatePath, cancellationToken)
                .ConfigureAwait(false)
            : null;
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
            Description = package.Manifest.Description,
            PackagePath = storedPath,
            Active = false
        };
        state.Mods.Add(installed);
        state.ModCatalogRevision = InstalledState.CurrentModCatalogRevision;
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

    public async Task RestoreVanillaAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        InstalledState state = await RequireInitializedStateAsync(cancellationToken).ConfigureAwait(false);
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
            RequireFreeSpace(installation, KnownGameBuilds.UsNaRetail.ArchiveSize + 128L * 1024L * 1024L);

            string stagedExecutable = Path.Combine(stageDirectory, "TMNT2.exe");
            string stagedArchive = Path.Combine(stageDirectory, "TMNT.DAT");
            string stagedFeatureState = Path.Combine(stageDirectory, "features.txt");
            CoreSelection? coreSelection = ResolveCoreSelection(packages);
            await BuildExecutableAsync(
                backup.ExecutablePath,
                stagedExecutable,
                coreSelection,
                stageDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);
            await BuildArchiveAsync(
                backup.ArchivePath,
                stagedArchive,
                packages,
                stageDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);

            byte[] featureState = ComposableFeatureState.Serialize(
                coreSelection?.CoreId,
                coreSelection?.FeatureFlags ?? []);
            await File.WriteAllBytesAsync(stagedFeatureState, featureState, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress("validate", "Validating staged game files…", 0.65));
            _ = PeValidator.ValidateWin32Executable(stagedExecutable);
            _ = AfsArchive.Read(stagedArchive);
            string executableHash = await Sha256Service.HashFileAsync(stagedExecutable, cancellationToken)
                .ConfigureAwait(false);
            string archiveHash = await Sha256Service.HashFileAsync(stagedArchive, cancellationToken)
                .ConfigureAwait(false);
            string featureStateHash = await Sha256Service.HashFileAsync(
                stagedFeatureState,
                cancellationToken).ConfigureAwait(false);

            await TransactionalFileInstaller.CommitAsync(
                installation,
                stagedExecutable,
                stagedArchive,
                stagedFeatureState,
                executableHash,
                archiveHash,
                featureStateHash,
                progress,
                cancellationToken).ConfigureAwait(false);

            foreach (InstalledMod mod in state.Mods)
            {
                mod.Active = desiredModIds.Contains(mod.Id);
            }
            state.CurrentExecutableSha256 = executableHash;
            state.CurrentArchiveSha256 = archiveHash;
            state.CurrentFeatureStateSha256 = featureStateHash;
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
        string stagedExecutable,
        CoreSelection? selection,
        string stageDirectory,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (selection is null)
        {
            progress?.Report(new OperationProgress("stage", "Staging the vanilla executable…", 0.1));
            await CopyFileAsync(vanillaExecutable, stagedExecutable, cancellationToken).ConfigureAwait(false);
            return;
        }

        T2ModPackage package = selection.Package;
        ModOperation operation = selection.Operation;
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

    private static CoreSelection? ResolveCoreSelection(IReadOnlyList<T2ModPackage> packages)
    {
        List<(T2ModPackage Package, ModOperation Operation)> operations = packages
            .SelectMany(package => package.Manifest.Operations
                .Where(operation => operation.Type == "apply-t2delta")
                .Select(operation => (package, operation)))
            .ToList();

        if (operations.Count == 0)
        {
            return null;
        }

        if (operations.Count == 1 && operations[0].Package.Manifest.Core is null)
        {
            return new CoreSelection(
                operations[0].Package,
                operations[0].Operation,
                null,
                []);
        }

        if (operations.Any(item => item.Package.Manifest.Core is null))
        {
            throw new InvalidOperationException(
                "Legacy standalone EXE packages cannot be combined with composable packages. " +
                "Re-import the composable package versions first.");
        }

        string[] coreIds = operations
            .Select(item => item.Package.Manifest.Core!.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (coreIds.Length != 1)
        {
            throw new InvalidOperationException(
                "The selected mods target different composable T2 Core families.");
        }

        IReadOnlyList<string> flags = ComposableFeatureState.CollectFlags(
            packages.Select(package => package.Manifest));
        List<(T2ModPackage Package, ModOperation Operation)> candidates = operations
            .Where(item => flags.All(flag =>
                item.Package.Manifest.Core!.Capabilities.Contains(
                    flag,
                    StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Package.Manifest.Core!.Priority)
            .ThenBy(item => item.Operation.ExpectedOutputSha256, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No installed package provides a T2 Core capable of running every selected feature.");
        }

        (T2ModPackage Package, ModOperation Operation) selected = candidates[0];
        return new CoreSelection(
            selected.Package,
            selected.Operation,
            coreIds[0],
            flags);
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

    private static string? TryReadInstalledDescription(InstalledMod mod)
    {
        try
        {
            using T2ModPackage package = T2ModPackage.Open(mod.PackagePath);
            if (package.Manifest.Id.Equals(mod.Id, StringComparison.Ordinal) &&
                package.Manifest.Version.Equals(mod.Version, StringComparison.Ordinal))
            {
                return package.Manifest.Description;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Keep the launcher usable; activation performs full package validation.
        }

        return null;
    }

    private async Task<InstalledState> RequireInitializedStateAsync(CancellationToken cancellationToken)
    {
        InstalledState state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(state.GameDirectory) || string.IsNullOrWhiteSpace(state.GameBuildId))
        {
            throw new InvalidOperationException("Select and verify the game folder first.");
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

        if (!string.IsNullOrWhiteSpace(state.CurrentFeatureStateSha256))
        {
            if (!File.Exists(installation.FeatureStatePath) ||
                !Sha256Service.EqualsHash(
                    await Sha256Service.HashFileAsync(
                        installation.FeatureStatePath,
                        cancellationToken).ConfigureAwait(false),
                    state.CurrentFeatureStateSha256))
            {
                throw new InvalidDataException(
                    "The managed feature-state file changed outside the manager. " +
                    "Use Verify/Repair instead of applying another mod state.");
            }
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

    private sealed record CoreSelection(
        T2ModPackage Package,
        ModOperation Operation,
        string? CoreId,
        IReadOnlyList<string> FeatureFlags);
}
