using T2ModManager.Core.Models;

namespace T2ModManager.Core.Game;

public static class GameDirectoryLocator
{
    public static string Normalize(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("Select a game folder first.", nameof(gameDirectory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory.Trim()));
    }

    public static bool ContainsRequiredFiles(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return false;
        }

        try
        {
            GameInstallation installation = new(Normalize(gameDirectory));
            return Directory.Exists(installation.RootDirectory) &&
                   File.Exists(installation.ExecutablePath) &&
                   File.Exists(installation.ArchivePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return Normalize(first).Equals(Normalize(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
