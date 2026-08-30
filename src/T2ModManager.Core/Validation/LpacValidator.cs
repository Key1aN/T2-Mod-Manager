using System.Buffers.Binary;
using System.Text;
using T2ModManager.Core.Hashing;

namespace T2ModManager.Core.Validation;

public sealed record LpacChunk(
    int Id,
    uint Type,
    string Name,
    long HeaderOffset,
    long DataOffset,
    long Size);

public sealed record LpacValidationResult(long FileSize, int ChunkCount, IReadOnlyList<LpacChunk> Chunks);

public static class LpacValidator
{
    private const int Alignment = 64;
    private const int MainHeaderSize = 32;
    private const int ExtraHeaderSize = 32;
    private const int MaximumChunks = 4096;
    private static readonly byte[] Magic = [0x4C, 0x50, 0x41, 0x43];

    public static LpacValidationResult Validate(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

        if (stream.Length < Alignment || !reader.ReadBytes(4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("LPAC magic is missing or the file is truncated.");
        }

        uint rawCount = reader.ReadUInt32();
        if (rawCount == 0 || rawCount > MaximumChunks)
        {
            throw new InvalidDataException($"Invalid LPAC chunk count: {rawCount}.");
        }

        int count = checked((int)rawCount);
        stream.Position = StreamCopy.Align(stream.Position, Alignment);
        List<LpacChunk> chunks = new(count);

        for (int id = 0; id < count; id++)
        {
            long headerOffset = stream.Position;
            if (headerOffset + MainHeaderSize + ExtraHeaderSize > stream.Length)
            {
                throw new InvalidDataException($"LPAC chunk {id} has a truncated header.");
            }

            byte[] mainHeader = reader.ReadBytes(MainHeaderSize);
            _ = reader.ReadBytes(ExtraHeaderSize);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(mainHeader.AsSpan(0, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(mainHeader.AsSpan(4, 4));
            string name = DecodeName(mainHeader.AsSpan(16, 16));
            long dataOffset = StreamCopy.Align(stream.Position, Alignment);
            long dataEnd = checked(dataOffset + size);

            if (dataEnd > stream.Length)
            {
                throw new InvalidDataException($"LPAC chunk {id} extends past the file.");
            }

            chunks.Add(new LpacChunk(id, type, name, headerOffset, dataOffset, size));
            stream.Position = StreamCopy.Align(dataEnd, Alignment);
            if (stream.Position > stream.Length)
            {
                throw new InvalidDataException($"LPAC chunk {id} has invalid alignment padding.");
            }
        }

        return new LpacValidationResult(stream.Length, count, chunks);
    }

    private static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        ReadOnlySpan<byte> name = terminator >= 0 ? bytes[..terminator] : bytes;
        foreach (byte value in name)
        {
            if (value is < 0x20 or > 0x7E)
            {
                throw new InvalidDataException("LPAC chunk name contains invalid ASCII bytes.");
            }
        }

        return Encoding.ASCII.GetString(name);
    }
}

