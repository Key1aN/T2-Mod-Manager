using T2ModManager.Core.Hashing;

namespace T2ModManager.Core.Archives;

public sealed record AfsEntry(int Id, long Offset, long Size);

public sealed record AfsArchiveInfo(
    long FileSize,
    int SectorSize,
    long HeaderExtent,
    IReadOnlyList<AfsEntry> Entries,
    long NameTableOffset,
    long NameTableSize);

public static class AfsArchive
{
    private const int SectorSize = 2048;
    private const int MaximumEntries = 100_000;
    private static readonly byte[] Magic = [0x41, 0x46, 0x53, 0x00];

    public static AfsArchiveInfo Read(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        if (stream.Length < 16 || !reader.ReadBytes(4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("TMNT.DAT does not have a valid AFS header.");
        }

        uint rawCount = reader.ReadUInt32();
        if (rawCount == 0 || rawCount > MaximumEntries)
        {
            throw new InvalidDataException($"Invalid AFS entry count: {rawCount}.");
        }

        int count = checked((int)rawCount);
        long descriptorTableEnd = checked(8L + ((long)count + 1L) * 8L);
        if (descriptorTableEnd > stream.Length)
        {
            throw new InvalidDataException("The AFS descriptor table is truncated.");
        }

        List<AfsEntry> entries = new(count);
        for (int id = 0; id < count; id++)
        {
            long offset = reader.ReadUInt32();
            long size = reader.ReadUInt32();
            entries.Add(new AfsEntry(id, offset, size));
        }

        long nameTableOffset = reader.ReadUInt32();
        long nameTableSize = reader.ReadUInt32();
        long headerExtent = entries.Min(entry => entry.Offset);

        ValidateLayout(
            stream.Length,
            descriptorTableEnd,
            headerExtent,
            entries,
            nameTableOffset,
            nameTableSize);

        return new AfsArchiveInfo(
            stream.Length,
            SectorSize,
            headerExtent,
            entries,
            nameTableOffset,
            nameTableSize);
    }

    public static async Task ExtractEntryAsync(
        string archivePath,
        int entryId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        AfsArchiveInfo info = Read(archivePath);
        if ((uint)entryId >= (uint)info.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryId));
        }

        AfsEntry entry = info.Entries[entryId];
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination has no parent directory."));

        await using FileStream source = new(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        source.Position = entry.Offset;
        await StreamCopy.CopyExactlyAsync(source, destination, entry.Size, cancellationToken)
            .ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AfsArchiveInfo> RebuildAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyDictionary<int, string> replacements,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AfsArchiveInfo sourceInfo = Read(sourcePath);
        foreach ((int id, string replacementPath) in replacements)
        {
            if ((uint)id >= (uint)sourceInfo.Entries.Count)
            {
                throw new InvalidDataException($"AFS replacement entry {id} is outside the archive.");
            }

            if (!File.Exists(replacementPath))
            {
                throw new FileNotFoundException("AFS replacement payload was not found.", replacementPath);
            }

            if (new FileInfo(replacementPath).Length > uint.MaxValue)
            {
                throw new InvalidDataException("An AFS replacement is too large for the format.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination has no parent directory."));
        if (File.Exists(destinationPath))
        {
            throw new IOException($"Staged archive already exists: {destinationPath}");
        }

        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        source.Position = 0;
        await StreamCopy.CopyExactlyAsync(
            source,
            destination,
            sourceInfo.HeaderExtent,
            cancellationToken).ConfigureAwait(false);

        List<AfsEntry> outputEntries = new(sourceInfo.Entries.Count);
        long totalInputBytes = sourceInfo.Entries.Sum(entry => entry.Size);
        long completedInputBytes = 0;

        foreach (AfsEntry entry in sourceInfo.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PadToSectorAsync(destination, cancellationToken).ConfigureAwait(false);
            long outputOffset = destination.Position;
            long outputSize;

            if (replacements.TryGetValue(entry.Id, out string? replacementPath))
            {
                await using FileStream replacement = new(
                    replacementPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                outputSize = replacement.Length;
                await replacement.CopyToAsync(destination, 1024 * 1024, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                source.Position = entry.Offset;
                outputSize = entry.Size;
                await StreamCopy.CopyExactlyAsync(source, destination, entry.Size, cancellationToken)
                    .ConfigureAwait(false);
            }

            outputEntries.Add(new AfsEntry(entry.Id, outputOffset, outputSize));
            completedInputBytes += entry.Size;
            progress?.Report(totalInputBytes == 0 ? 1.0 : (double)completedInputBytes / totalInputBytes);
        }

        await PadToSectorAsync(destination, cancellationToken).ConfigureAwait(false);
        long outputNameTableOffset = destination.Position;
        source.Position = sourceInfo.NameTableOffset;
        await StreamCopy.CopyExactlyAsync(
            source,
            destination,
            sourceInfo.NameTableSize,
            cancellationToken).ConfigureAwait(false);
        await PadToSectorAsync(destination, cancellationToken).ConfigureAwait(false);

        destination.Position = 8;
        using (BinaryWriter writer = new(destination, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            foreach (AfsEntry entry in outputEntries)
            {
                writer.Write(checked((uint)entry.Offset));
                writer.Write(checked((uint)entry.Size));
            }

            writer.Write(checked((uint)outputNameTableOffset));
            writer.Write(checked((uint)sourceInfo.NameTableSize));
            writer.Flush();
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        progress?.Report(1.0);

        return new AfsArchiveInfo(
            destination.Length,
            SectorSize,
            sourceInfo.HeaderExtent,
            outputEntries,
            outputNameTableOffset,
            sourceInfo.NameTableSize);
    }

    private static void ValidateLayout(
        long fileLength,
        long descriptorTableEnd,
        long headerExtent,
        IReadOnlyList<AfsEntry> entries,
        long nameTableOffset,
        long nameTableSize)
    {
        if (headerExtent < descriptorTableEnd || headerExtent % SectorSize != 0)
        {
            throw new InvalidDataException("The AFS header extent is invalid.");
        }

        long previousEnd = headerExtent;
        foreach (AfsEntry entry in entries)
        {
            if (entry.Offset % SectorSize != 0 || entry.Offset < previousEnd)
            {
                throw new InvalidDataException($"AFS entry {entry.Id} has an invalid or overlapping offset.");
            }

            long end = checked(entry.Offset + entry.Size);
            if (end > fileLength)
            {
                throw new InvalidDataException($"AFS entry {entry.Id} extends past the archive.");
            }

            previousEnd = end;
        }

        if (nameTableOffset % SectorSize != 0 || nameTableSize < 0)
        {
            throw new InvalidDataException("The AFS name table descriptor is invalid.");
        }

        long nameTableEnd = checked(nameTableOffset + nameTableSize);
        if (nameTableOffset < previousEnd || nameTableEnd > fileLength)
        {
            throw new InvalidDataException("The AFS name table overlaps data or extends past the archive.");
        }
    }

    private static async Task PadToSectorAsync(
        Stream destination,
        CancellationToken cancellationToken)
    {
        long aligned = StreamCopy.Align(destination.Position, SectorSize);
        await StreamCopy.WriteZerosAsync(
            destination,
            aligned - destination.Position,
            cancellationToken).ConfigureAwait(false);
    }
}
