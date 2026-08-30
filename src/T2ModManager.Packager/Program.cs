using System.IO.Compression;
using T2ModManager.Core.Archives;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;
using T2ModManager.Core.Patching;
using T2ModManager.Core.Validation;

return await ProgramMain.RunAsync(args);

internal static class ProgramMain
{
    private const int SlashuurEntryId = 143;
    private const int FinalSlashuurChunkCount = 77;
    private const string OriginalSlashuurHash =
        "e3f93aa1e63efefb403854a5eea603cc322a9de6b953de89292e804e76777451";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Dictionary<string, string> options = ParseOptions(args);
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (!string.Equals(args[0], "pack-ultimate-slashuur", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown command: {args[0]}");
            }

            string originalExe = RequireOption(options, "--original-exe");
            string standaloneExe = RequireOption(options, "--standalone-exe");
            string originalDat = RequireOption(options, "--original-dat");
            string finalLpac = RequireOption(options, "--slashuur-lpac");
            string output = RequireOption(options, "--output");
            await PackUltimateSlashuurAsync(
                originalExe,
                standaloneExe,
                originalDat,
                finalLpac,
                output).ConfigureAwait(false);
            Console.WriteLine($"Created: {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static async Task PackUltimateSlashuurAsync(
        string originalExe,
        string standaloneExe,
        string originalDat,
        string finalLpac,
        string output)
    {
        if (File.Exists(output))
        {
            throw new IOException("Refusing to overwrite an existing output package.");
        }

        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        await RequireFingerprintAsync(
            originalExe,
            known.ExecutableSize,
            known.ExecutableSha256).ConfigureAwait(false);
        await RequireFingerprintAsync(
            originalDat,
            known.ArchiveSize,
            known.ArchiveSha256).ConfigureAwait(false);
        _ = PeValidator.ValidateWin32Executable(standaloneExe);

        LpacValidationResult lpac = LpacValidator.Validate(finalLpac);
        if (lpac.ChunkCount != FinalSlashuurChunkCount)
        {
            throw new InvalidDataException(
                $"Final Ultimate_Slashuur LPAC must contain {FinalSlashuurChunkCount} chunks; found {lpac.ChunkCount}.");
        }

        AfsArchiveInfo archive = AfsArchive.Read(originalDat);
        AfsEntry originalEntry = archive.Entries[SlashuurEntryId];
        string originalEntryHash = await Sha256Service.HashRangeAsync(
            originalDat,
            originalEntry.Offset,
            originalEntry.Size).ConfigureAwait(false);
        if (originalEntry.Size != 605_632 ||
            !Sha256Service.EqualsHash(originalEntryHash, OriginalSlashuurHash))
        {
            throw new InvalidDataException("Original Slashuur AFS entry does not match the verified baseline.");
        }

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "T2-Mod-Packager-v0.1",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string deltaPath = Path.Combine(temporaryDirectory, "Ultimate_Slashuur.t2delta");
            Console.WriteLine("Generating verified executable delta…");
            await T2Delta.GenerateAsync(originalExe, standaloneExe, deltaPath).ConfigureAwait(false);

            string deltaHash = await Sha256Service.HashFileAsync(deltaPath).ConfigureAwait(false);
            string standaloneHash = await Sha256Service.HashFileAsync(standaloneExe).ConfigureAwait(false);
            string lpacHash = await Sha256Service.HashFileAsync(finalLpac).ConfigureAwait(false);

            ModManifest manifest = BuildManifest(
                deltaPath,
                deltaHash,
                standaloneExe,
                standaloneHash,
                finalLpac,
                lpacHash);
            byte[] manifestJson = ManifestSerializer.Serialize(manifest);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using FileStream packageStream = new(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using ZipArchive package = new(packageStream, ZipArchiveMode.Create, leaveOpen: false);
            AddBytes(package, "manifest.json", manifestJson);
            AddFile(package, "payload/Ultimate_Slashuur.t2delta", deltaPath, CompressionLevel.NoCompression);
            AddFile(package, "payload/Ultimate_Slashuur.lpac", finalLpac, CompressionLevel.Optimal);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static ModManifest BuildManifest(
        string deltaPath,
        string deltaHash,
        string standaloneExe,
        string standaloneHash,
        string finalLpac,
        string lpacHash)
    {
        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        return new ModManifest
        {
            SchemaVersion = 1,
            Id = "Ultimate_Slashuur",
            DisplayName = "Ultimate Slashuur",
            Version = "1.0.0",
            Description = "Playable Slashuur with five boss moves and Adaptive Teleport v2. " +
                          "Does not enable MSAA, Intro Skip, widescreen, or difficulty modifications.",
            Target = new GameTarget
            {
                Game = "TMNT2_Battle_Nexus_PC",
                Region = known.Region,
                Architecture = known.Architecture,
                ExecutableSha256 = known.ExecutableSha256,
                ArchiveSha256 = known.ArchiveSha256
            },
            Resources =
            [
                new ResourceClaim { Id = "file:TMNT2.exe" },
                new ResourceClaim { Id = "afs:TMNT.DAT:143" },
                new ResourceClaim { Id = "feature:playable-slashuur" },
                new ResourceClaim { Id = "exe:function:CSlashuur::OnTeleport" }
            ],
            Operations =
            [
                new ModOperation
                {
                    Type = "apply-t2delta",
                    Target = "TMNT2.exe",
                    Payload = "payload/Ultimate_Slashuur.t2delta",
                    PayloadSha256 = deltaHash,
                    PayloadSize = new FileInfo(deltaPath).Length,
                    ExpectedInputSha256 = known.ExecutableSha256,
                    ExpectedInputSize = known.ExecutableSize,
                    ExpectedOutputSha256 = standaloneHash,
                    ExpectedOutputSize = new FileInfo(standaloneExe).Length
                },
                new ModOperation
                {
                    Type = "replace-afs-entry",
                    Target = "TMNT.DAT",
                    Payload = "payload/Ultimate_Slashuur.lpac",
                    PayloadSha256 = lpacHash,
                    PayloadSize = new FileInfo(finalLpac).Length,
                    EntryId = SlashuurEntryId,
                    LogicalPath = "player/slashuur/slashuur.lpac",
                    ExpectedInputSha256 = OriginalSlashuurHash,
                    ExpectedInputSize = 605_632,
                    ExpectedOutputSha256 = lpacHash,
                    ExpectedOutputSize = new FileInfo(finalLpac).Length
                }
            ]
        };
    }

    private static async Task RequireFingerprintAsync(string path, long size, string hash)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size)
        {
            throw new InvalidDataException($"Unexpected size: {path}");
        }

        string actual = await Sha256Service.HashFileAsync(path).ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(actual, hash))
        {
            throw new InvalidDataException($"Unexpected SHA-256: {path}");
        }
    }

    private static void AddBytes(ZipArchive archive, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open();
        stream.Write(bytes);
    }

    private static void AddFile(ZipArchive archive, string path, string source, CompressionLevel level)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, level);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream input = File.OpenRead(source);
        using Stream output = entry.Open();
        input.CopyTo(output);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            }
            result.Add(args[index], args[index + 1]);
        }
        return result;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option: {name}");

    private static void PrintUsage()
    {
        Console.WriteLine("T2-Mod-Packager-v0.1");
        Console.WriteLine();
        Console.WriteLine("pack-ultimate-slashuur \\");
        Console.WriteLine("  --original-exe <untouched US TMNT2.exe> \\");
        Console.WriteLine("  --standalone-exe <Ultimate_Slashuur-only TMNT2.exe> \\");
        Console.WriteLine("  --original-dat <untouched US TMNT.DAT> \\");
        Console.WriteLine("  --slashuur-lpac <final 77-chunk slashuur.lpac> \\");
        Console.WriteLine("  --output <Ultimate_Slashuur.t2mod>");
    }
}

