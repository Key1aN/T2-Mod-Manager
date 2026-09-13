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
    private const int DefaultSlashuurChunkCount = 77;
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

            switch (args[0])
            {
                case "pack-ultimate-slashuur":
                    await PackUltimateSlashuurAsync(
                        RequireOption(options, "--original-exe"),
                        RequireOption(options, "--standalone-exe"),
                        RequireOption(options, "--original-dat"),
                        RequireOption(options, "--slashuur-lpac"),
                        RequireOption(options, "--output")).ConfigureAwait(false);
                    break;

                case "pack-exe-only":
                    await PackExeOnlyAsync(
                        RequireOption(options, "--original-exe"),
                        RequireOption(options, "--standalone-exe"),
                        RequireOption(options, "--original-dat"),
                        RequireOption(options, "--id"),
                        RequireOption(options, "--display-name"),
                        RequireOption(options, "--version"),
                        RequireOption(options, "--description"),
                        RequireOption(options, "--feature-resource"),
                        RequireOption(options, "--output"),
                        composable: null).ConfigureAwait(false);
                    break;

                case "pack-composable-exe":
                    await PackExeOnlyAsync(
                        RequireOption(options, "--original-exe"),
                        RequireOption(options, "--core-exe"),
                        RequireOption(options, "--original-dat"),
                        RequireOption(options, "--id"),
                        RequireOption(options, "--display-name"),
                        RequireOption(options, "--version"),
                        RequireOption(options, "--description"),
                        RequireOption(options, "--feature-resource"),
                        RequireOption(options, "--output"),
                        ParseComposableDefinition(options)).ConfigureAwait(false);
                    break;

                case "pack-composable-ultimate-slashuur":
                    await PackUltimateSlashuurAsync(
                        RequireOption(options, "--original-exe"),
                        RequireOption(options, "--core-exe"),
                        RequireOption(options, "--original-dat"),
                        RequireOption(options, "--slashuur-lpac"),
                        RequireOption(options, "--output"),
                        RequireOption(options, "--version"),
                        RequireOption(options, "--description"),
                        ParseComposableDefinition(options),
                        ParseOptionalPositiveInt(
                            options,
                            "--slashuur-chunk-count",
                            DefaultSlashuurChunkCount)).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentException($"Unknown command: {args[0]}");
            }

            Console.WriteLine($"Created: {Path.GetFullPath(RequireOption(options, "--output"))}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static async Task PackExeOnlyAsync(
        string originalExe,
        string standaloneExe,
        string originalDat,
        string id,
        string displayName,
        string version,
        string description,
        string featureResource,
        string output,
        ComposablePackageDefinition? composable)
    {
        if (File.Exists(output))
        {
            throw new IOException("Refusing to overwrite an existing output package.");
        }

        RequireSafeIdentifier(id, "mod ID");
        if (!featureResource.StartsWith("feature:", StringComparison.Ordinal) ||
            featureResource.Length > 180 ||
            featureResource.Any(char.IsControl))
        {
            throw new ArgumentException("The feature resource must be a safe feature: resource ID.");
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

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "T2-Mod-Packager-v0.2-EXE-Only",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string payloadName = $"{id}.t2delta";
            string payloadPath = $"payload/{payloadName}";
            string deltaPath = Path.Combine(temporaryDirectory, payloadName);
            Console.WriteLine("Generating verified executable delta…");
            await T2Delta.GenerateAsync(originalExe, standaloneExe, deltaPath).ConfigureAwait(false);

            string deltaHash = await Sha256Service.HashFileAsync(deltaPath).ConfigureAwait(false);
            string standaloneHash = await Sha256Service.HashFileAsync(standaloneExe).ConfigureAwait(false);
            ModManifest manifest = new()
            {
                SchemaVersion = 1,
                Id = id,
                DisplayName = displayName,
                Version = version,
                Description = description,
                Target = new GameTarget
                {
                    Game = "TMNT2_Battle_Nexus_PC",
                    Region = known.Region,
                    Architecture = known.Architecture,
                    ExecutableSha256 = known.ExecutableSha256,
                    ArchiveSha256 = known.ArchiveSha256
                },
                Core = composable?.Core,
                FeatureFlags = composable?.FeatureFlags ?? [],
                Resources =
                [
                    new ResourceClaim { Id = "file:TMNT2.exe" },
                    new ResourceClaim { Id = featureResource }
                ],
                Operations =
                [
                    new ModOperation
                    {
                        Type = "apply-t2delta",
                        Target = "TMNT2.exe",
                        Payload = payloadPath,
                        PayloadSha256 = deltaHash,
                        PayloadSize = new FileInfo(deltaPath).Length,
                        ExpectedInputSha256 = known.ExecutableSha256,
                        ExpectedInputSize = known.ExecutableSize,
                        ExpectedOutputSha256 = standaloneHash,
                        ExpectedOutputSize = new FileInfo(standaloneExe).Length
                    }
                ]
            };
            byte[] manifestJson = ManifestSerializer.Serialize(manifest);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using FileStream packageStream = new(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using ZipArchive package = new(packageStream, ZipArchiveMode.Create, leaveOpen: false);
            AddBytes(package, "manifest.json", manifestJson);
            AddFile(package, payloadPath, deltaPath, CompressionLevel.NoCompression);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task PackUltimateSlashuurAsync(
        string originalExe,
        string standaloneExe,
        string originalDat,
        string finalLpac,
        string output,
        string version = "1.0.0",
        string description = "Playable Slashuur with five boss moves and Adaptive Teleport v2. " +
                             "Does not enable MSAA, Intro Skip, widescreen, or difficulty modifications.",
        ComposablePackageDefinition? composable = null,
        int expectedLpacChunkCount = DefaultSlashuurChunkCount)
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
        if (lpac.ChunkCount != expectedLpacChunkCount)
        {
            throw new InvalidDataException(
                $"Final Ultimate_Slashuur LPAC must contain {expectedLpacChunkCount} chunks; found {lpac.ChunkCount}.");
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
            "T2-Mod-Packager-v0.2-Ultimate-Slashuur",
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
                lpacHash,
                version,
                description,
                composable);
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
        string lpacHash,
        string version,
        string description,
        ComposablePackageDefinition? composable)
    {
        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        return new ModManifest
        {
            SchemaVersion = 1,
            Id = "Ultimate_Slashuur",
            DisplayName = "Ultimate Slashuur",
            Version = version,
            Description = description,
            Target = new GameTarget
            {
                Game = "TMNT2_Battle_Nexus_PC",
                Region = known.Region,
                Architecture = known.Architecture,
                ExecutableSha256 = known.ExecutableSha256,
                ArchiveSha256 = known.ArchiveSha256
            },
            Core = composable?.Core,
            FeatureFlags = composable?.FeatureFlags ?? [],
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

    private static int ParseOptionalPositiveInt(
        IReadOnlyDictionary<string, string> options,
        string name,
        int defaultValue)
    {
        if (!options.TryGetValue(name, out string? value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int result) || result <= 0)
        {
            throw new ArgumentException($"{name} must be a positive integer.");
        }

        return result;
    }

    private static void RequireSafeIdentifier(string value, string description)
    {
        if (value.Length is < 1 or > 80 ||
            !char.IsLetterOrDigit(value[0]) ||
            value.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '_' and not '.' and not '-'))
        {
            throw new ArgumentException($"Invalid {description}: {value}");
        }
    }

    private static ComposablePackageDefinition ParseComposableDefinition(
        IReadOnlyDictionary<string, string> options)
    {
        string coreId = RequireOption(options, "--core-id");
        RequireSafeIdentifier(coreId, "composable core ID");
        if (!int.TryParse(RequireOption(options, "--core-priority"), out int priority) ||
            priority is < 0 or > 1_000)
        {
            throw new ArgumentException("The composable core priority must be from 0 through 1000.");
        }

        List<string> capabilities = ParseIdentifierList(
            RequireOption(options, "--core-capabilities"),
            "core capability");
        List<string> featureFlags = ParseIdentifierList(
            RequireOption(options, "--feature-flags"),
            "feature flag");
        if (featureFlags.Any(flag =>
            !capabilities.Contains(flag, StringComparer.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Every feature flag must be present in the core capability list.");
        }

        return new ComposablePackageDefinition(
            new ComposableCore
            {
                Id = coreId,
                Priority = priority,
                Capabilities = capabilities
            },
            featureFlags);
    }

    private static List<string> ParseIdentifierList(string value, string description)
    {
        List<string> result = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (result.Count is < 1 or > 32 ||
            result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
        {
            throw new ArgumentException($"The {description} list is empty, duplicated, or too large.");
        }
        foreach (string item in result)
        {
            RequireSafeIdentifier(item, description);
        }
        return result;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("T2-Mod-Packager-v0.2-Composable-Mods-US");
        Console.WriteLine();
        Console.WriteLine("pack-ultimate-slashuur \\");
        Console.WriteLine("  --original-exe <untouched US TMNT2.exe> \\");
        Console.WriteLine("  --standalone-exe <Ultimate_Slashuur-only TMNT2.exe> \\");
        Console.WriteLine("  --original-dat <untouched US TMNT.DAT> \\");
        Console.WriteLine("  --slashuur-lpac <final Slashuur LPAC> \\");
        Console.WriteLine("  --output <Ultimate_Slashuur.t2mod>");
        Console.WriteLine();
        Console.WriteLine("pack-exe-only \\");
        Console.WriteLine("  --original-exe <untouched US TMNT2.exe> \\");
        Console.WriteLine("  --standalone-exe <feature-only US x86 executable> \\");
        Console.WriteLine("  --original-dat <untouched US TMNT.DAT> \\");
        Console.WriteLine("  --id <package_id> \\");
        Console.WriteLine("  --display-name <display name> \\");
        Console.WriteLine("  --version <numeric dotted version> \\");
        Console.WriteLine("  --description <package description> \\");
        Console.WriteLine("  --feature-resource <feature:resource-id> \\");
        Console.WriteLine("  --output <package.t2mod>");
        Console.WriteLine();
        Console.WriteLine("pack-composable-exe \\");
        Console.WriteLine("  --original-exe <untouched US TMNT2.exe> \\");
        Console.WriteLine("  --core-exe <shared composable-core US x86 executable> \\");
        Console.WriteLine("  --original-dat <untouched US TMNT.DAT> \\");
        Console.WriteLine("  --id <package_id> --display-name <name> --version <version> \\");
        Console.WriteLine("  --description <description> --feature-resource <feature:id> \\");
        Console.WriteLine("  --core-id <core_family> --core-priority <0..1000> \\");
        Console.WriteLine("  --core-capabilities <comma-separated IDs> \\");
        Console.WriteLine("  --feature-flags <comma-separated IDs> --output <package.t2mod>");
        Console.WriteLine();
        Console.WriteLine("pack-composable-ultimate-slashuur \\");
        Console.WriteLine("  --original-exe <untouched US TMNT2.exe> \\");
        Console.WriteLine("  --core-exe <Slashuur-capable composable-core executable> \\");
        Console.WriteLine("  --original-dat <untouched US TMNT.DAT> \\");
        Console.WriteLine("  --slashuur-lpac <final Slashuur LPAC> \\");
        Console.WriteLine("  --slashuur-chunk-count <expected count; defaults to 77> \\");
        Console.WriteLine("  --version <version> --description <description> \\");
        Console.WriteLine("  --core-id <core_family> --core-priority <0..1000> \\");
        Console.WriteLine("  --core-capabilities <comma-separated IDs> \\");
        Console.WriteLine("  --feature-flags <comma-separated IDs> --output <Ultimate_Slashuur.t2mod>");
    }

    private sealed record ComposablePackageDefinition(
        ComposableCore Core,
        List<string> FeatureFlags);
}

