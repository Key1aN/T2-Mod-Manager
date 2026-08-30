using System.Security.Cryptography;
using System.Text.Json;
using T2ModManager.Core.Archives;
using T2ModManager.Core.Conflict;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;
using T2ModManager.Core.Patching;
using T2ModManager.Core.Transactions;
using T2ModManager.Core.Validation;

string root = Path.Combine(Path.GetTempPath(), $"cmm-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    await DeltaRoundTripAsync(root);
    await AfsReplacementRoundTripAsync(root);
    LpacValidation(root);
    ConflictDetection();
    CompatibilityStateRoundTrip();
    LegacyStateRemainsReadable();
    CompatibilityIdentity();
    await ExecutableOnlyTransactionLeavesArchiveUntouchedAsync(root);
    Console.WriteLine("All T2 Mod Manager v0.1 core tests passed.");
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

static void ConflictDetection()
{
    ModManifest first = Manifest("first", "afs:TMNT.DAT:143");
    ModManifest second = Manifest("second", "afs:TMNT.DAT:143");
    IReadOnlyList<ModConflict> conflicts = ConflictDetector.FindConflicts([first, second]);
    Assert(conflicts.Count == 1 && conflicts[0].ResourceId == "afs:TMNT.DAT:143", "Exclusive conflict was not detected.");
}

static void CompatibilityStateRoundTrip()
{
    KnownCompatibilityExecutable known = KnownCompatibilityExecutables.ObsoleteDiscCheckFixUsNa;
    InstalledState source = new()
    {
        CompatibilityExecutable = new CompatibilityExecutableState
        {
            Id = known.Id,
            DisplayName = known.DisplayName,
            Size = known.Size,
            Sha256 = known.Sha256,
            Enabled = true
        }
    };
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(source, ManifestSerializer.JsonOptions);
    InstalledState restored = JsonSerializer.Deserialize<InstalledState>(
        json,
        ManifestSerializer.JsonOptions) ?? throw new InvalidDataException("State did not deserialize.");
    Assert(
        restored.CompatibilityExecutable is { Enabled: true } compatibility &&
        compatibility.Id == known.Id &&
        compatibility.Size == known.Size &&
        compatibility.Sha256 == known.Sha256,
        "Compatibility executable state did not round trip.");
}

static void LegacyStateRemainsReadable()
{
    const string legacyJson = """
        {
          "schemaVersion": 1,
          "mods": []
        }
        """;
    InstalledState restored = JsonSerializer.Deserialize<InstalledState>(
        legacyJson,
        ManifestSerializer.JsonOptions) ?? throw new InvalidDataException("Legacy state did not deserialize.");
    Assert(restored.CompatibilityExecutable is null, "Legacy state unexpectedly enabled a compatibility EXE.");
}

static void CompatibilityIdentity()
{
    KnownCompatibilityExecutable known = KnownCompatibilityExecutables.ObsoleteDiscCheckFixUsNa;
    Assert(known.Size == 4_796_416, "Compatibility EXE size changed.");
    Assert(
        known.Sha256 == "86d508d0ba5905cb457577a3a43c3e397de5e835e2a221c97c3caca7132069ab",
        "Compatibility EXE allowlist hash changed.");
}

static async Task ExecutableOnlyTransactionLeavesArchiveUntouchedAsync(string root)
{
    string gameDirectory = Path.Combine(root, "exe-only-game");
    string stagingDirectory = Path.Combine(gameDirectory, "stage-input");
    Directory.CreateDirectory(gameDirectory);
    Directory.CreateDirectory(stagingDirectory);
    GameInstallation installation = new(gameDirectory);
    byte[] originalExecutable = RandomNumberGenerator.GetBytes(4096);
    byte[] replacementExecutable = RandomNumberGenerator.GetBytes(4096);
    byte[] originalArchive = RandomNumberGenerator.GetBytes(8192);
    await File.WriteAllBytesAsync(installation.ExecutablePath, originalExecutable);
    await File.WriteAllBytesAsync(installation.ArchivePath, originalArchive);
    string stagedExecutable = Path.Combine(stagingDirectory, "TMNT2.exe");
    await File.WriteAllBytesAsync(stagedExecutable, replacementExecutable);
    string expectedExecutableHash = await Sha256Service.HashFileAsync(stagedExecutable);
    string expectedArchiveHash = await Sha256Service.HashFileAsync(installation.ArchivePath);

    await TransactionalFileInstaller.CommitExecutableAsync(
        installation,
        stagedExecutable,
        expectedExecutableHash,
        expectedArchiveHash);

    Assert(
        (await File.ReadAllBytesAsync(installation.ExecutablePath)).SequenceEqual(replacementExecutable),
        "EXE-only transaction did not install the executable.");
    Assert(
        (await File.ReadAllBytesAsync(installation.ArchivePath)).SequenceEqual(originalArchive),
        "EXE-only transaction changed the archive.");
    await TransactionalFileInstaller.CleanupCompletedAsync(installation);
}

static ModManifest Manifest(string id, string resource) => new()
{
    SchemaVersion = 1,
    Id = id,
    DisplayName = id,
    Version = "1.0.0",
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
