using System.IO.Compression;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;

namespace T2ModManager.Core.Packages;

public sealed class T2ModPackage : IDisposable
{
    private const long MaximumManifestSize = 1024 * 1024;
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    private T2ModPackage(
        string packagePath,
        FileStream stream,
        ZipArchive archive,
        Dictionary<string, ZipArchiveEntry> entries,
        ModManifest manifest)
    {
        PackagePath = packagePath;
        _stream = stream;
        _archive = archive;
        _entries = entries;
        Manifest = manifest;
    }

    public string PackagePath { get; }
    public ModManifest Manifest { get; }

    public static T2ModPackage Open(string packagePath)
    {
        FileStream stream = new(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
            Dictionary<string, ZipArchiveEntry> entries = ValidateEntries(archive);
            if (!entries.TryGetValue("manifest.json", out ZipArchiveEntry? manifestEntry) ||
                manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestSize)
            {
                archive.Dispose();
                throw new InvalidDataException("Package manifest.json is missing or too large.");
            }

            byte[] manifestBytes = new byte[checked((int)manifestEntry.Length)];
            using (Stream manifestStream = manifestEntry.Open())
            {
                manifestStream.ReadExactly(manifestBytes);
            }

            ModManifest manifest = ManifestSerializer.Deserialize(manifestBytes);
            foreach (ModOperation operation in manifest.Operations)
            {
                if (!entries.TryGetValue(operation.Payload, out ZipArchiveEntry? payload) ||
                    payload.Length != operation.PayloadSize)
                {
                    archive.Dispose();
                    throw new InvalidDataException($"Payload is missing or has the wrong size: {operation.Payload}");
                }
            }

            return new T2ModPackage(packagePath, stream, archive, entries, manifest);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async Task ExtractVerifiedPayloadAsync(
        ModOperation operation,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(operation.Payload, out ZipArchiveEntry? entry))
        {
            throw new InvalidDataException($"Package payload not found: {operation.Payload}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Payload destination has no parent directory."));

        await using (Stream source = entry.Open())
        await using (FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(destination, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        FileInfo result = new(destinationPath);
        if (result.Length != operation.PayloadSize)
        {
            throw new InvalidDataException($"Extracted payload size mismatch: {operation.Payload}");
        }

        string hash = await Sha256Service.HashFileAsync(destinationPath, cancellationToken)
            .ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(hash, operation.PayloadSha256))
        {
            throw new InvalidDataException($"Extracted payload hash mismatch: {operation.Payload}");
        }
    }

    public void Dispose()
    {
        _archive.Dispose();
        _stream.Dispose();
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntries(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            string[] segments = path.Split('/', StringSplitOptions.None);
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains(":", StringComparison.Ordinal) ||
                segments.Any(segment => segment is "." or "..") ||
                unixType == 0xA000)
            {
                throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
            }

            if (path.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!entries.TryAdd(path, entry))
            {
                throw new InvalidDataException($"Duplicate ZIP entry: {path}");
            }
        }

        return entries;
    }
}
