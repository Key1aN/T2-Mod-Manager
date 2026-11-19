using System.Text;

namespace T2ModManager.Core.Validation;

/// <summary>Validation for AFS entries that are not LPAC containers (e.g. RenderWare Rt2d font
/// metrics, fonts/*.met). Their integrity is already guaranteed by the payload/output SHA-256
/// checks; this only rejects obviously wrong content for the known file kinds.</summary>
public static class AfsPayloadValidator
{
    private const long MaximumMetricsSize = 1024 * 1024;

    public static void ValidateNonLpac(string logicalPath, string payloadPath)
    {
        long size = new FileInfo(payloadPath).Length;
        if (size <= 0)
        {
            throw new InvalidDataException($"AFS payload for {logicalPath} is empty.");
        }

        if (logicalPath.EndsWith(".met", StringComparison.OrdinalIgnoreCase))
        {
            if (size > MaximumMetricsSize)
            {
                throw new InvalidDataException($"Font metrics {logicalPath} is too large.");
            }

            using FileStream stream = File.OpenRead(payloadPath);
            byte[] magic = new byte[7];
            int read = stream.Read(magic, 0, magic.Length);
            if (read != magic.Length || Encoding.ASCII.GetString(magic) != "METRICS")
            {
                throw new InvalidDataException($"Font metrics {logicalPath} does not start with METRICS.");
            }

            return;
        }

        throw new InvalidDataException(
            $"AFS entry {logicalPath} is not an LPAC and has no validator in this manager version.");
    }
}
