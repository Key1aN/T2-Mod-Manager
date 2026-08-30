using System.Buffers;
using System.Security.Cryptography;

namespace T2ModManager.Core.Hashing;

public static class Sha256Service
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await HashStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> HashStreamAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<string> HashRangeAsync(
        string path,
        long offset,
        long length,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (offset + length > stream.Length)
        {
            throw new InvalidDataException("Requested hash range is outside the file.");
        }

        stream.Position = offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long remaining = length;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of file while hashing a range.");
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static bool EqualsHash(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

