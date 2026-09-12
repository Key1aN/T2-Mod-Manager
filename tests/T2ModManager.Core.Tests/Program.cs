using System.Security.Cryptography;
using T2ModManager.Core.Archives;
using T2ModManager.Core.Conflict;
using T2ModManager.Core.Game;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;
using T2ModManager.Core.Patching;
using T2ModManager.Core.State;
using T2ModManager.Core.Validation;

string root = Path.Combine(Path.GetTempPath(), $"cmm-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
string? integrationGameDirectory = null;
IReadOnlyList<string> packagePaths = args;
if (args.Length >= 3 && args[0] == "--integration-game")
{
    integrationGameDirectory = args[1];
    packagePaths = args.Skip(2).ToArray();
}

try
{
    await DeltaRoundTripAsync(root);
    await AfsReplacementRoundTripAsync(root);
    foreach (string packagePath in packagePaths)
    {
        await PackageValidationAsync(root, packagePath);
    }
    if (integrationGameDirectory is not null)
    {
        bool allComposable = packagePaths.Count > 0 && packagePaths.All(path =>
        {
            using T2ModPackage package = T2ModPackage.Open(path);
            return package.Manifest.Core is not null;
        });
        if (allComposable)
        {
            await ComposablePackageIntegrationAsync(root, integrationGameDirectory, packagePaths);
        }
        else
        {
            await ExeOnlyPackageIntegrationAsync(root, integrationGameDirectory, packagePaths);
        }
    }
    GameDirectoryDetection(root);
    ManifestDescriptionValidation();
    ModCatalogMigration();
    await ModCatalogMigrationPersistenceAsync(root);
    LpacValidation(root);
    ConflictDetection();
    ComposableManifestValidation();
    Console.WriteLine("All T2 Mod Manager v0.2 composable-mod core tests passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    Directory.Delete(root, recursive: true);
}

static async Task DeltaRoundTripAsync(string root)
{
    string source = Path.Combine(root, "delta-source.bin");
    string target = Path.Combine(root, "delta-target.bin");
    string delta = Path.Combine(root, "roundtrip.t2delta");
    string output = Path.Combine(root, "delta-output.bin");
    byte[] sourceBytes = RandomNumberGenerator.GetBytes(128 * 1024);
    byte[] targetBytes = (byte[])sourceBytes.Clone();
    RandomNumberGenerator.Fill(targetBytes.AsSpan(32_000, 7_000));
    Array.Resize(ref targetBytes, targetBytes.Length + 333);
    RandomNumberGenerator.Fill(targetBytes.AsSpan(targetBytes.Length - 333));
    await File.WriteAllBytesAsync(source, sourceBytes);
    await File.WriteAllBytesAsync(target, targetBytes);

    await T2Delta.GenerateAsync(source, target, delta);
    await T2Delta.ApplyAsync(source, delta, output);
    Assert((await File.ReadAllBytesAsync(output)).SequenceEqual(targetBytes), "Delta round trip changed bytes.");
}

static async Task AfsReplacementRoundTripAsync(string root)
{
    string source = Path.Combine(root, "source.afs");
    string rebuilt = Path.Combine(root, "rebuilt.afs");
    string replacement = Path.Combine(root, "replacement.bin");
    string extracted = Path.Combine(root, "extracted.bin");
    byte[] original = "original-entry"u8.ToArray();
    byte[] changed = "replacement-entry-with-a-new-size"u8.ToArray();
    CreateSyntheticAfs(source, original);
    await File.WriteAllBytesAsync(replacement, changed);

    await AfsArchive.RebuildAsync(source, rebuilt, new Dictionary<int, string> { [0] = replacement });
    await AfsArchive.ExtractEntryAsync(rebuilt, 0, extracted);
    Assert((await File.ReadAllBytesAsync(extracted)).SequenceEqual(changed), "AFS replacement did not round trip.");
}

static void LpacValidation(string root)
{
    string path = Path.Combine(root, "sample.lpac");
    byte[] bytes = new byte[192];
    "LPAC"u8.CopyTo(bytes);
    BitConverter.GetBytes(1u).CopyTo(bytes, 4);
    BitConverter.GetBytes(7u).CopyTo(bytes, 64);
    BitConverter.GetBytes(3u).CopyTo(bytes, 68);
    "test"u8.CopyTo(bytes.AsSpan(80));
    bytes[128] = 1;
    bytes[129] = 2;
    bytes[130] = 3;
    File.WriteAllBytes(path, bytes);
    LpacValidationResult result = LpacValidator.Validate(path);
    Assert(result.ChunkCount == 1 && result.Chunks[0].Name == "test", "LPAC metadata was parsed incorrectly.");
}

static async Task ExeOnlyPackageIntegrationAsync(
    string root,
    string sourceGameDirectory,
    IReadOnlyList<string> packagePaths)
{
    string gameDirectory = Path.Combine(root, "integration-game");
    Directory.CreateDirectory(gameDirectory);
    string gameExe = Path.Combine(gameDirectory, "TMNT2.exe");
    string gameDat = Path.Combine(gameDirectory, "TMNT.DAT");
    File.Copy(Path.Combine(sourceGameDirectory, "TMNT2.exe"), gameExe);
    File.Copy(Path.Combine(sourceGameDirectory, "TMNT.DAT"), gameDat);

    KnownGameBuild known = KnownGameBuilds.UsNaRetail;
    ModManagerService service = new(Path.Combine(root, "integration-appdata"));
    await service.InitializeGameAsync(gameDirectory);

    foreach (string packagePath in packagePaths)
    {
        using T2ModPackage package = T2ModPackage.Open(packagePath);
        Assert(
            package.Manifest.Operations.Count == 1 &&
            package.Manifest.Operations[0].Type == "apply-t2delta",
            $"Package {package.Manifest.Id} is not EXE-only.");
        string expectedOutputHash = package.Manifest.Operations[0].ExpectedOutputSha256!;

        InstalledMod installed = await service.InstallPackageAsync(packagePath);
        await service.ActivateAsync(installed.Id);
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameExe), expectedOutputHash),
            $"Package {installed.Id} did not produce its declared executable.");
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameDat), known.ArchiveSha256),
            $"Package {installed.Id} changed TMNT.DAT.");

        await service.DeactivateAsync(installed.Id);
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameExe), known.ExecutableSha256),
            $"Deactivating {installed.Id} did not restore the retail executable.");
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameDat), known.ArchiveSha256),
            $"Deactivating {installed.Id} did not preserve the retail archive.");
    }
}

static async Task ComposablePackageIntegrationAsync(
    string root,
    string sourceGameDirectory,
    IReadOnlyList<string> packagePaths)
{
    string gameDirectory = Path.Combine(root, "composable-integration-game");
    Directory.CreateDirectory(gameDirectory);
    string gameExe = Path.Combine(gameDirectory, "TMNT2.exe");
    string gameDat = Path.Combine(gameDirectory, "TMNT.DAT");
    File.Copy(Path.Combine(sourceGameDirectory, "TMNT2.exe"), gameExe);
    File.Copy(Path.Combine(sourceGameDirectory, "TMNT.DAT"), gameDat);

    List<ModManifest> manifests = [];
    foreach (string packagePath in packagePaths)
    {
        using T2ModPackage package = T2ModPackage.Open(packagePath);
        manifests.Add(package.Manifest);
    }

    Assert(
        ConflictDetector.FindConflicts(manifests).Count == 0,
        "Compatible composable packages were reported as conflicting.");

    ModManagerService service = new(Path.Combine(root, "composable-integration-appdata"));
    await service.InitializeGameAsync(gameDirectory);
    foreach (string packagePath in packagePaths)
    {
        _ = await service.InstallPackageAsync(packagePath);
    }

    List<string> activeIds = [];
    foreach (ModManifest manifest in manifests)
    {
        await service.ActivateAsync(manifest.Id);
        activeIds.Add(manifest.Id);
        await VerifyComposableStateAsync(service, gameExe, gameDat, activeIds, manifests);
    }

    for (int index = activeIds.Count - 1; index >= 0; --index)
    {
        string id = activeIds[index];
        await service.DeactivateAsync(id);
        activeIds.RemoveAt(index);
        await VerifyComposableStateAsync(service, gameExe, gameDat, activeIds, manifests);
    }
}

static async Task VerifyComposableStateAsync(
    ModManagerService service,
    string gameExe,
    string gameDat,
    IReadOnlyCollection<string> activeIds,
    IReadOnlyList<ModManifest> manifests)
{
    KnownGameBuild known = KnownGameBuilds.UsNaRetail;
    List<ModManifest> active = manifests
        .Where(manifest => activeIds.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
        .ToList();
    InstalledState state = await service.GetStateAsync();
    Assert(
        state.Mods.Where(mod => mod.Active).Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(activeIds),
        "The active package catalog does not match the requested composable set.");

    if (active.Count == 0)
    {
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameExe), known.ExecutableSha256),
            "Removing the final composable mod did not restore the retail executable.");
    }
    else
    {
        IReadOnlyList<string> flags = ComposableFeatureState.CollectFlags(active);
        ModManifest selectedCore = active
            .Where(manifest => flags.All(flag => manifest.Core!.Capabilities.Contains(
                flag,
                StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(manifest => manifest.Core!.Priority)
            .ThenBy(manifest => manifest.Operations.Single(operation =>
                operation.Type == "apply-t2delta").ExpectedOutputSha256,
                StringComparer.OrdinalIgnoreCase)
            .First();
        string expectedExeHash = selectedCore.Operations.Single(operation =>
            operation.Type == "apply-t2delta").ExpectedOutputSha256!;
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameExe), expectedExeHash),
            "The launcher did not select the expected composable core executable.");
    }

    string featureStatePath = Path.Combine(Path.GetDirectoryName(gameExe)!, ".t2-mod-manager", "features.txt");
    string? coreId = active.Count == 0 ? null : active[0].Core!.Id;
    byte[] expectedFeatureState = ComposableFeatureState.Serialize(
        coreId,
        ComposableFeatureState.CollectFlags(active));
    Assert(
        (await File.ReadAllBytesAsync(featureStatePath)).SequenceEqual(expectedFeatureState),
        "The installed composable feature-state file is incorrect.");

    ModOperation? replacement = active
        .SelectMany(manifest => manifest.Operations)
        .SingleOrDefault(operation => operation.Type == "replace-afs-entry");
    if (replacement is null)
    {
        Assert(
            Sha256Service.EqualsHash(await Sha256Service.HashFileAsync(gameDat), known.ArchiveSha256),
            "An EXE-only composable combination changed TMNT.DAT.");
    }
    else
    {
        AfsArchiveInfo archive = AfsArchive.Read(gameDat);
        AfsEntry entry = archive.Entries[replacement.EntryId!.Value];
        string hash = await Sha256Service.HashRangeAsync(gameDat, entry.Offset, entry.Size);
        Assert(
            entry.Size == replacement.ExpectedOutputSize &&
            Sha256Service.EqualsHash(hash, replacement.ExpectedOutputSha256!),
            "The composable archive replacement was not preserved.");
    }
}

static async Task PackageValidationAsync(string root, string packagePath)
{
    using T2ModPackage package = T2ModPackage.Open(packagePath);
    Assert(
        !string.IsNullOrWhiteSpace(package.Manifest.Description),
        "The supplied package description is missing.");
    Assert(
        !string.IsNullOrWhiteSpace(package.Manifest.Id),
        "The supplied package identity is missing.");

    foreach (ModOperation operation in package.Manifest.Operations)
    {
        string output = Path.Combine(root, Path.GetFileName(operation.Payload));
        await package.ExtractVerifiedPayloadAsync(operation, output);
    }
}

static void GameDirectoryDetection(string root)
{
    string arbitraryName = Path.Combine(root, "Any Game Folder Name");
    Directory.CreateDirectory(arbitraryName);
    File.WriteAllBytes(Path.Combine(arbitraryName, "TMNT2.exe"), [0x4D, 0x5A]);
    File.WriteAllBytes(Path.Combine(arbitraryName, "TMNT.DAT"), "AFS\0"u8.ToArray());

    Assert(
        GameDirectoryLocator.ContainsRequiredFiles(arbitraryName),
        "A game folder with an arbitrary directory name was rejected.");
    Assert(
        GameDirectoryLocator.PathsEqual(arbitraryName, arbitraryName + Path.DirectorySeparatorChar),
        "Equivalent game directory paths were not normalized consistently.");

    File.Delete(Path.Combine(arbitraryName, "TMNT.DAT"));
    Assert(
        !GameDirectoryLocator.ContainsRequiredFiles(arbitraryName),
        "A folder missing TMNT.DAT was accepted.");
}

static void ConflictDetection()
{
    ModManifest first = Manifest("first", "afs:TMNT.DAT:143");
    ModManifest second = Manifest("second", "afs:TMNT.DAT:143");
    IReadOnlyList<ModConflict> conflicts = ConflictDetector.FindConflicts([first, second]);
    Assert(conflicts.Count == 1 && conflicts[0].ResourceId == "afs:TMNT.DAT:143", "Exclusive conflict was not detected.");

    ModManifest composableFirst = ComposableManifest("composable-first", "shared-core", "intro-skip");
    ModManifest composableSecond = ComposableManifest("composable-second", "shared-core", "msaa");
    Assert(
        ConflictDetector.FindConflicts([composableFirst, composableSecond]).Count == 0,
        "Packages in the same composable core family conflicted on TMNT2.exe.");

    ModManifest otherCore = ComposableManifest("other-core", "different-core", "widescreen");
    Assert(
        ConflictDetector.FindConflicts([composableFirst, otherCore]).Any(conflict =>
            conflict.ResourceId.Equals("file:TMNT2.exe", StringComparison.OrdinalIgnoreCase)),
        "Packages from different core families did not conflict on TMNT2.exe.");
}

static void ComposableManifestValidation()
{
    ModManifest manifest = ComposableManifest("valid-composable", "shared-core", "intro-skip");
    ManifestSerializer.Validate(manifest);

    ModManifest invalidFlag = ComposableManifest("invalid-flag", "shared-core", "intro-skip");
    invalidFlag.Core!.Capabilities.Clear();
    invalidFlag.Core.Capabilities.Add("msaa");
    AssertThrows<InvalidDataException>(
        () => ManifestSerializer.Validate(invalidFlag),
        "A feature flag absent from core capabilities was accepted.");

    byte[] serialized = ComposableFeatureState.Serialize(
        "shared-core",
        ["widescreen", "intro-skip"]);
    string text = System.Text.Encoding.UTF8.GetString(serialized);
    Assert(
        text.IndexOf("feature=intro-skip", StringComparison.Ordinal) <
        text.IndexOf("feature=widescreen", StringComparison.Ordinal),
        "Composable feature flags were not serialized deterministically.");
}

static void ManifestDescriptionValidation()
{
    ModManifest missingDescription = Manifest(
        "missing-description",
        "feature:test-description",
        description: "   ");
    AssertThrows<InvalidDataException>(
        () => ManifestSerializer.Validate(missingDescription),
        "A package without a meaningful description was accepted.");
}

static void ModCatalogMigration()
{
    InstalledState legacy = new();
    legacy.Mods.Add(Installed("legacy-inactive", active: false));
    legacy.Mods.Add(Installed("legacy-active", active: true));

    Assert(StateMigration.PrepareModCatalog(legacy), "Legacy catalog migration did not run.");
    Assert(
        legacy.ModCatalogRevision == InstalledState.CurrentModCatalogRevision,
        "Catalog migration revision was not recorded.");
    Assert(
        legacy.Mods.Count == 1 && legacy.Mods[0].Id == "legacy-active",
        "Legacy inactive entries were not cleared safely.");

    legacy.Mods.Add(Installed("new-import", active: false));
    Assert(
        !StateMigration.PrepareModCatalog(legacy),
        "Catalog migration ran more than once.");
    Assert(
        legacy.Mods.Any(mod => mod.Id == "new-import"),
        "A package imported after migration did not persist.");
}

static InstalledMod Installed(string id, bool active) => new()
{
    Id = id,
    DisplayName = id,
    Version = "1.0.0",
    Description = "Test package description",
    PackagePath = id + ".t2mod",
    Active = active
};

static async Task ModCatalogMigrationPersistenceAsync(string root)
{
    string stateRoot = Path.Combine(root, "catalog-state");
    AppDataPaths paths = new(stateRoot);
    StateStore store = new(paths);
    InstalledState legacy = new();
    legacy.Mods.Add(Installed("legacy-inactive", active: false));
    await store.SaveAsync(legacy);

    ModManagerService service = new(stateRoot);
    InstalledState migrated = await service.GetStateAsync();
    Assert(migrated.Mods.Count == 0, "The first loaded Mods view was not empty.");

    InstalledState persisted = await store.LoadAsync();
    Assert(
        persisted.ModCatalogRevision == InstalledState.CurrentModCatalogRevision,
        "The catalog migration was not persisted.");

    persisted.Mods.Add(Installed("new-import", active: false));
    await store.SaveAsync(persisted);
    InstalledState reloaded = await service.GetStateAsync();
    Assert(
        reloaded.Mods.Any(mod => mod.Id == "new-import"),
        "An imported package was cleared after the one-time migration.");
}

static ModManifest Manifest(
    string id,
    string resource,
    string description = "Test package description") => new()
{
    SchemaVersion = 1,
    Id = id,
    DisplayName = id,
    Version = "1.0.0",
    Description = description,
    Target = new GameTarget
    {
        Game = "TMNT2: Battle Nexus",
        Region = "US/NA",
        Architecture = "x86",
        ExecutableSha256 = new('0', 64),
        ArchiveSha256 = new('1', 64)
    },
    Resources = [new ResourceClaim { Id = resource }]
};

static ModManifest ComposableManifest(string id, string coreId, string featureFlag) => new()
{
    SchemaVersion = 1,
    Id = id,
    DisplayName = id,
    Version = "2.0.0",
    Description = "Composable test package",
    Target = new GameTarget
    {
        Game = "TMNT2_Battle_Nexus_PC",
        Region = "US/NA",
        Architecture = "x86",
        ExecutableSha256 = new('0', 64),
        ArchiveSha256 = new('1', 64)
    },
    Core = new ComposableCore
    {
        Id = coreId,
        Priority = 10,
        Capabilities = ["intro-skip", "msaa", "widescreen"]
    },
    FeatureFlags = [featureFlag],
    Resources =
    [
        new ResourceClaim { Id = "file:TMNT2.exe" },
        new ResourceClaim { Id = $"feature:{id}" }
    ],
    Operations =
    [
        new ModOperation
        {
            Type = "apply-t2delta",
            Target = "TMNT2.exe",
            Payload = $"payload/{id}.t2delta",
            PayloadSha256 = new('2', 64),
            PayloadSize = 1,
            ExpectedInputSha256 = new('0', 64),
            ExpectedInputSize = 1,
            ExpectedOutputSha256 = new('3', 64),
            ExpectedOutputSize = 1
        }
    ]
};

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void CreateSyntheticAfs(string path, byte[] entry)
{
    const int header = 2048;
    const int nameOffset = 4096;
    const int nameSize = 48;
    byte[] archive = new byte[6144];
    "AFS\0"u8.CopyTo(archive);
    BitConverter.GetBytes(1u).CopyTo(archive, 4);
    BitConverter.GetBytes((uint)header).CopyTo(archive, 8);
    BitConverter.GetBytes((uint)entry.Length).CopyTo(archive, 12);
    BitConverter.GetBytes((uint)nameOffset).CopyTo(archive, 16);
    BitConverter.GetBytes((uint)nameSize).CopyTo(archive, 20);
    entry.CopyTo(archive, header);
    "entry.bin"u8.CopyTo(archive.AsSpan(nameOffset));
    File.WriteAllBytes(path, archive);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
