using System.IO.Compression;
using System.Security.Cryptography;
using T2ModManager.Core.Hashing;

namespace T2ModManager.Core.Patching;

public static class T2Delta
{
    private static readonly byte[] Magic = "T2DELTA1"u8.ToArray();
    private const uint FormatVersion = 1;
    private const int MatchBlockSize = 32;
    private const int SourceStride = 16;
    private const int MinimumMatch = 64;
    private const int MaximumDataChunk = 1024 * 1024;
    private const int MaximumInputSize = 256 * 1024 * 1024;
    private const int MaximumInstructions = 10_000_000;

    private enum Opcode : byte
    {
        Copy = 0,
        Data = 1
    }

    private sealed record Instruction(Opcode Opcode, int SourceOffset, int Length, byte[]? Data);

    public static async Task GenerateAsync(
        string sourcePath,
        string targetPath,
        string deltaPath,
        CancellationToken cancellationToken = default)
    {
        FileInfo sourceInfo = new(sourcePath);
        FileInfo targetInfo = new(targetPath);
        if (sourceInfo.Length > MaximumInputSize || targetInfo.Length > MaximumInputSize)
        {
            throw new InvalidDataException("T2DELTA1 generation is limited to 256 MiB inputs.");
        }

        byte[] source = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        byte[] target = await File.ReadAllBytesAsync(targetPath, cancellationToken).ConfigureAwait(false);
        List<Instruction> instructions = BuildInstructions(source, target, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(deltaPath)
            ?? throw new InvalidOperationException("Delta destination has no parent directory."));

        await using FileStream output = new(
            deltaPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using BinaryWriter writer = new(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write((long)source.Length);
        writer.Write((long)target.Length);
        writer.Write(SHA256.HashData(source));
        writer.Write(SHA256.HashData(target));
        writer.Write(instructions.Count);

        foreach (Instruction instruction in instructions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write((byte)instruction.Opcode);
            switch (instruction.Opcode)
            {
                case Opcode.Copy:
                    writer.Write((long)instruction.SourceOffset);
                    writer.Write(instruction.Length);
                    break;

                case Opcode.Data:
                    byte[] raw = instruction.Data
                        ?? throw new InvalidOperationException("Data instruction has no bytes.");
                    byte[] compressed = Compress(raw);
                    writer.Write(raw.Length);
                    writer.Write(compressed.Length);
                    writer.Write(compressed);
                    break;

                default:
                    throw new InvalidOperationException("Unknown in-memory delta opcode.");
            }
        }

        writer.Flush();
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    public static async Task ApplyAsync(
        string sourcePath,
        string deltaPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        await using FileStream delta = new(
            deltaPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using BinaryReader reader = new(delta, System.Text.Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) || reader.ReadUInt32() != FormatVersion)
        {
            throw new InvalidDataException("Unsupported or corrupt T2DELTA1 header.");
        }

        long sourceLength = reader.ReadInt64();
        long targetLength = reader.ReadInt64();
        byte[] expectedSourceHash = reader.ReadBytes(32);
        byte[] expectedTargetHash = reader.ReadBytes(32);
        int instructionCount = reader.ReadInt32();

        if (sourceLength != source.Length || targetLength < 0 || targetLength > MaximumInputSize ||
            expectedSourceHash.Length != 32 || expectedTargetHash.Length != 32 ||
            instructionCount <= 0 || instructionCount > MaximumInstructions)
        {
            throw new InvalidDataException("T2DELTA1 metadata is invalid for this source.");
        }

        source.Position = 0;
        string actualSourceHash = await Sha256Service.HashStreamAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(actualSourceHash, Convert.ToHexString(expectedSourceHash)))
        {
            throw new InvalidDataException("T2DELTA1 source hash mismatch.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Delta output has no parent directory."));
        if (File.Exists(outputPath))
        {
            throw new IOException($"Delta output already exists: {outputPath}");
        }

        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        for (int index = 0; index < instructionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Opcode opcode = (Opcode)reader.ReadByte();
            switch (opcode)
            {
                case Opcode.Copy:
                    long sourceOffset = reader.ReadInt64();
                    int copyLength = reader.ReadInt32();
                    if (sourceOffset < 0 || copyLength <= 0 || sourceOffset + copyLength > source.Length)
                    {
                        throw new InvalidDataException($"Invalid copy instruction at index {index}.");
                    }

                    EnsureTargetBound(output.Position, copyLength, targetLength);
                    source.Position = sourceOffset;
                    await StreamCopy.CopyExactlyAsync(source, output, copyLength, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case Opcode.Data:
                    int rawLength = reader.ReadInt32();
                    int compressedLength = reader.ReadInt32();
                    if (rawLength <= 0 || rawLength > MaximumDataChunk ||
                        compressedLength <= 0 || compressedLength > MaximumDataChunk * 2L ||
                        compressedLength > delta.Length - delta.Position)
                    {
                        throw new InvalidDataException($"Invalid data instruction at index {index}.");
                    }

                    EnsureTargetBound(output.Position, rawLength, targetLength);
                    byte[] compressed = reader.ReadBytes(compressedLength);
                    if (compressed.Length != compressedLength)
                    {
                        throw new EndOfStreamException("T2DELTA1 data instruction is truncated.");
                    }

                    long before = output.Position;
                    using (MemoryStream compressedStream = new(compressed, writable: false))
                    using (BrotliStream decompressor = new(compressedStream, CompressionMode.Decompress))
                    {
                        await decompressor.CopyToAsync(output, 128 * 1024, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (output.Position - before != rawLength)
                    {
                        throw new InvalidDataException($"Data instruction {index} decompressed to the wrong size.");
                    }
                    break;

                default:
                    throw new InvalidDataException($"Unknown T2DELTA1 opcode at instruction {index}.");
            }
        }

        if (output.Position != targetLength || delta.Position != delta.Length)
        {
            throw new InvalidDataException("T2DELTA1 output length or trailing data is invalid.");
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Position = 0;
        string actualTargetHash = await Sha256Service.HashStreamAsync(output, cancellationToken)
            .ConfigureAwait(false);
        if (!Sha256Service.EqualsHash(actualTargetHash, Convert.ToHexString(expectedTargetHash)))
        {
            throw new InvalidDataException("T2DELTA1 target hash mismatch.");
        }

        output.Flush(flushToDisk: true);
    }

    private static List<Instruction> BuildInstructions(
        byte[] source,
        byte[] target,
        CancellationToken cancellationToken)
    {
        Dictionary<ulong, List<int>> sourceIndex = BuildSourceIndex(source);
        List<Instruction> instructions = [];
        List<byte> pendingData = new(MaximumDataChunk);

        int targetOffset = 0;
        while (targetOffset < target.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int sourceOffset, int length) = FindBestMatch(source, target, targetOffset, sourceIndex);
            if (length >= MinimumMatch)
            {
                FlushData(instructions, pendingData);
                AddCopy(instructions, sourceOffset, length);
                targetOffset += length;
                continue;
            }

            pendingData.Add(target[targetOffset]);
            targetOffset++;
            if (pendingData.Count == MaximumDataChunk)
            {
                FlushData(instructions, pendingData);
            }
        }

        FlushData(instructions, pendingData);
        return instructions;
    }

    private static Dictionary<ulong, List<int>> BuildSourceIndex(byte[] source)
    {
        Dictionary<ulong, List<int>> index = [];
        for (int offset = 0; offset + MatchBlockSize <= source.Length; offset += SourceStride)
        {
            ulong hash = HashBlock(source.AsSpan(offset, MatchBlockSize));
            if (!index.TryGetValue(hash, out List<int>? offsets))
            {
                offsets = [];
                index.Add(hash, offsets);
            }

            if (offsets.Count < 32)
            {
                offsets.Add(offset);
            }
        }

        return index;
    }

    private static (int SourceOffset, int Length) FindBestMatch(
        byte[] source,
        byte[] target,
        int targetOffset,
        IReadOnlyDictionary<ulong, List<int>> sourceIndex)
    {
        if (targetOffset + MatchBlockSize > target.Length)
        {
            return (-1, 0);
        }

        ulong hash = HashBlock(target.AsSpan(targetOffset, MatchBlockSize));
        if (!sourceIndex.TryGetValue(hash, out List<int>? candidates))
        {
            return (-1, 0);
        }

        int bestSource = -1;
        int bestLength = 0;
        foreach (int candidate in candidates)
        {
            if (!source.AsSpan(candidate, MatchBlockSize)
                .SequenceEqual(target.AsSpan(targetOffset, MatchBlockSize)))
            {
                continue;
            }

            int length = MatchBlockSize;
            int maximum = Math.Min(source.Length - candidate, target.Length - targetOffset);
            while (length < maximum && source[candidate + length] == target[targetOffset + length])
            {
                length++;
            }

            if (length > bestLength)
            {
                bestSource = candidate;
                bestLength = length;
            }
        }

        return (bestSource, bestLength);
    }

    private static ulong HashBlock(ReadOnlySpan<byte> block)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        foreach (byte value in block)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private static void FlushData(List<Instruction> instructions, List<byte> pendingData)
    {
        if (pendingData.Count == 0)
        {
            return;
        }

        byte[] data = pendingData.ToArray();
        instructions.Add(new Instruction(Opcode.Data, 0, data.Length, data));
        pendingData.Clear();
    }

    private static void AddCopy(List<Instruction> instructions, int sourceOffset, int length)
    {
        if (instructions.Count > 0)
        {
            Instruction previous = instructions[^1];
            if (previous.Opcode == Opcode.Copy && previous.SourceOffset + previous.Length == sourceOffset)
            {
                instructions[^1] = previous with { Length = previous.Length + length };
                return;
            }
        }

        instructions.Add(new Instruction(Opcode.Copy, sourceOffset, length, null));
    }

    private static byte[] Compress(byte[] data)
    {
        using MemoryStream output = new();
        using (BrotliStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    private static void EnsureTargetBound(long position, long length, long targetLength)
    {
        if (position < 0 || length < 0 || position + length > targetLength)
        {
            throw new InvalidDataException("T2DELTA1 instruction exceeds the declared target length.");
        }
    }
}

