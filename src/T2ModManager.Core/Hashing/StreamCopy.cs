using System.Buffers;

namespace T2ModManager.Core.Hashing;

public static class StreamCopy
{
    private const int BufferSize = 1024 * 1024;

    public static async Task CopyExactlyAsync(
        Stream source,
        Stream destination,
        long bytes,
        CancellationToken cancellationToken = default)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long remaining = bytes;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of source stream.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task WriteZerosAsync(
        Stream destination,
        long bytes,
        CancellationToken cancellationToken = default)
    {
        if (bytes <= 0)
        {
            return;
        }

        byte[] zeroes = ArrayPool<byte>.Shared.Rent((int)Math.Min(BufferSize, bytes));
        Array.Clear(zeroes, 0, zeroes.Length);
        try
        {
            long remaining = bytes;
            while (remaining > 0)
            {
                int count = (int)Math.Min(zeroes.Length, remaining);
                await destination.WriteAsync(zeroes.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeroes);
        }
    }

    public static long Align(long value, int alignment)
    {
        if (value < 0 || alignment <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }
}
