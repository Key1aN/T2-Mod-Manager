namespace T2ModManager.Core.Diagnostics;

/// <summary>
/// Small append-only, thread-safe text log stored in the portable data folder
/// (logs\manager-YYYYMMDD.log). Logging never throws into the caller.
/// </summary>
public static class ManagerLog
{
    public const int RetainedLogFiles = 30;
    private static readonly object Gate = new();
    private static string? _directory;

    public static string? LogDirectory => _directory;

    public static string? CurrentLogPath =>
        _directory is null ? null : Path.Combine(_directory, $"manager-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize(string logsDirectory)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            _directory = logsDirectory;
            Prune(logsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _directory = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        string? path = CurrentLogPath;
        if (path is null)
        {
            return;
        }

        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never break an installation operation.
            }
        }
    }

    private static void Prune(string logsDirectory)
    {
        foreach (FileInfo old in new DirectoryInfo(logsDirectory)
                     .GetFiles("manager-*.log")
                     .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(RetainedLogFiles))
        {
            try
            {
                old.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
