using System.Buffers.Binary;

namespace T2ModManager.Core.Validation;

public sealed record PeValidationResult(
    ushort Machine,
    ushort SectionCount,
    ushort OptionalHeaderMagic,
    ushort Subsystem,
    long FileSize);

public static class PeValidator
{
    private const ushort Intel386 = 0x014C;
    private const ushort Pe32 = 0x010B;

    public static PeValidationResult ValidateWin32Executable(string path)
    {
        byte[] header = new byte[Math.Min(new FileInfo(path).Length, 4096)];
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.ReadExactly(header);
        }

        if (header.Length < 256 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            throw new InvalidDataException("The executable does not have a valid DOS header.");
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x3C, 4));
        if (peOffset < 0x40 || peOffset + 92 > header.Length)
        {
            throw new InvalidDataException("The PE header offset is invalid.");
        }

        ReadOnlySpan<byte> pe = header.AsSpan(peOffset);
        if (pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
        {
            throw new InvalidDataException("The executable does not have a PE signature.");
        }

        ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(pe.Slice(4, 2));
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.Slice(6, 2));
        ushort optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(pe.Slice(20, 2));
        if (machine != Intel386 || sectionCount == 0 || optionalHeaderSize < 70)
        {
            throw new InvalidDataException("The executable is not the expected Win32 x86 PE format.");
        }

        if (24L + optionalHeaderSize > pe.Length)
        {
            throw new InvalidDataException("The PE optional header is truncated.");
        }

        ReadOnlySpan<byte> optional = pe.Slice(24, optionalHeaderSize);
        ushort optionalMagic = BinaryPrimitives.ReadUInt16LittleEndian(optional.Slice(0, 2));
        ushort subsystem = BinaryPrimitives.ReadUInt16LittleEndian(optional.Slice(68, 2));
        if (optionalMagic != Pe32)
        {
            throw new InvalidDataException("The executable is not a PE32 image.");
        }

        return new PeValidationResult(
            machine,
            sectionCount,
            optionalMagic,
            subsystem,
            new FileInfo(path).Length);
    }
}
