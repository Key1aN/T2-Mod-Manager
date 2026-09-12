using System.Text;
using T2ModManager.Core.Models;

namespace T2ModManager.Core.Game;

public static class ComposableFeatureState
{
    public const string FileHeader = "# T2 Mod Manager composable feature state v1";

    public static IReadOnlyList<string> CollectFlags(IEnumerable<ModManifest> manifests) =>
        manifests
            .SelectMany(manifest => manifest.FeatureFlags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static byte[] Serialize(string? coreId, IReadOnlyList<string> flags)
    {
        StringBuilder text = new();
        text.AppendLine(FileHeader);
        if (!string.IsNullOrWhiteSpace(coreId))
        {
            text.Append("core=").AppendLine(coreId);
        }
        foreach (string flag in flags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase))
        {
            text.Append("feature=").AppendLine(flag);
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
