using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;

namespace T2ModManager.Core.Packages;

public static partial class ManifestSerializer
{
    public const int SupportedSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ModManifest Deserialize(ReadOnlySpan<byte> json)
    {
        ModManifest manifest = JsonSerializer.Deserialize<ModManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The mod manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    public static byte[] Serialize(ModManifest manifest)
    {
        Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
    }

    public static void Validate(ModManifest manifest)
    {
        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported manifest schema {manifest.SchemaVersion}; expected {SupportedSchemaVersion}.");
        }

        ValidateIdentifier(manifest.Id, "mod ID");
        if (string.IsNullOrWhiteSpace(manifest.DisplayName) || manifest.DisplayName.Length > 120)
        {
            throw new InvalidDataException("The mod display name is invalid.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Description) ||
            manifest.Description.Length > 2_000 ||
            manifest.Description.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidDataException(
                "The mod description is required and must not exceed 2,000 characters.");
        }

        if (!Version.TryParse(manifest.Version, out _))
        {
            throw new InvalidDataException("The mod version must use numeric dotted version syntax.");
        }

        if (!string.Equals(manifest.Target.Game, "TMNT2_Battle_Nexus_PC", StringComparison.Ordinal) ||
            !string.Equals(manifest.Target.Region, "US/NA", StringComparison.Ordinal) ||
            !string.Equals(manifest.Target.Architecture, "x86", StringComparison.Ordinal))
        {
            throw new InvalidDataException("This manager version accepts only TMNT2 US/NA x86 packages.");
        }

        RequireHash(manifest.Target.ExecutableSha256, "target executable hash");
        RequireHash(manifest.Target.ArchiveSha256, "target archive hash");

        HashSet<string> resourceIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResourceClaim resource in manifest.Resources)
        {
            ValidateResourceId(resource.Id);
            if (!string.Equals(resource.Mode, "exclusive", StringComparison.Ordinal))
            {
                throw new InvalidDataException("v0.1 supports only exclusive resource claims.");
            }

            if (!resourceIds.Add(resource.Id))
            {
                throw new InvalidDataException($"Duplicate resource claim: {resource.Id}");
            }
        }

        foreach (string conflict in manifest.Conflicts)
        {
            ValidateIdentifier(conflict, "conflict mod ID");
        }

        foreach (ModDependency dependency in manifest.Dependencies)
        {
            ValidateIdentifier(dependency.Id, "dependency mod ID");
            if (string.IsNullOrWhiteSpace(dependency.VersionRange) || dependency.VersionRange.Length > 40)
            {
                throw new InvalidDataException("A dependency version range is invalid.");
            }
        }

        if (manifest.Operations.Count == 0)
        {
            throw new InvalidDataException("A mod package must contain at least one operation.");
        }

        foreach (ModOperation operation in manifest.Operations)
        {
            ValidateOperation(operation, resourceIds);
        }
    }

    private static void ValidateOperation(ModOperation operation, IReadOnlySet<string> resourceIds)
    {
        ValidatePayloadPath(operation.Payload);
        RequireHash(operation.PayloadSha256, "payload hash");
        if (operation.PayloadSize <= 0 || operation.PayloadSize > 1024L * 1024L * 1024L)
        {
            throw new InvalidDataException("Payload size is invalid or exceeds the v0.1 limit.");
        }

        switch (operation.Type)
        {
            case "apply-t2delta":
                if (!string.Equals(operation.Target, "TMNT2.exe", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("apply-t2delta may target only TMNT2.exe in v0.1.");
                }

                RequireInputAndOutput(operation);
                RequireResource(resourceIds, "file:TMNT2.exe");
                break;

            case "replace-afs-entry":
                if (!string.Equals(operation.Target, "TMNT.DAT", StringComparison.OrdinalIgnoreCase) ||
                    operation.EntryId is null or < 0 || string.IsNullOrWhiteSpace(operation.LogicalPath))
                {
                    throw new InvalidDataException("replace-afs-entry requires TMNT.DAT, an entry ID, and a logical path.");
                }

                RequireInputAndOutput(operation);
                RequireResource(resourceIds, $"afs:TMNT.DAT:{operation.EntryId.Value}");
                break;

            default:
                throw new InvalidDataException($"Unsupported operation type: {operation.Type}");
        }
    }

    private static void RequireInputAndOutput(ModOperation operation)
    {
        RequireHash(operation.ExpectedInputSha256, "operation input hash");
        RequireHash(operation.ExpectedOutputSha256, "operation output hash");
        if (operation.ExpectedInputSize is null or < 0 || operation.ExpectedOutputSize is null or < 0)
        {
            throw new InvalidDataException("Operation input and output sizes are required.");
        }
    }

    private static void RequireResource(IReadOnlySet<string> resources, string expected)
    {
        if (!resources.Contains(expected))
        {
            throw new InvalidDataException($"Operation requires exclusive resource claim '{expected}'.");
        }
    }

    private static void RequireHash(string? value, string description)
    {
        if (!Sha256Service.IsSha256(value))
        {
            throw new InvalidDataException($"The {description} is not a SHA-256 value.");
        }
    }

    private static void ValidateIdentifier(string value, string description)
    {
        if (!IdentifierRegex().IsMatch(value))
        {
            throw new InvalidDataException($"Invalid {description}: {value}");
        }
    }

    private static void ValidateResourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180 ||
            value.Any(character => char.IsControl(character)))
        {
            throw new InvalidDataException("A resource ID is invalid.");
        }
    }

    public static void ValidatePayloadPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.None);
        if (!normalized.StartsWith("payload/", StringComparison.Ordinal) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(":", StringComparison.Ordinal) ||
            segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe package payload path: {path}");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
