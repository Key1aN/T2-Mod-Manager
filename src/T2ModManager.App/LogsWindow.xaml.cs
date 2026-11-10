using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using T2ModManager.Core.Diagnostics;
using T2ModManager.Core.Models;
using T2ModManager.Core.State;

namespace T2ModManager.App;

public partial class LogsWindow : Window
{
    private static readonly Brush CrashBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x7A));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB5));

    private readonly AppDataPaths _paths;
    private InstalledState _state;
    private bool _busy;

    public LogsWindow(AppDataPaths paths, InstalledState state)
    {
        _paths = paths;
        _state = state;
        InitializeComponent();
        Reload(state);
    }

    public void Reload(InstalledState state)
    {
        _state = state;
        string? selectedPath = (FilesList.SelectedItem as LogEntry)?.File.FullPath;
        List<LogEntry> entries = DiagnosticFiles.Find(_paths, _state.GameDirectory)
            .Select(file => new LogEntry(file))
            .ToList();
        FilesList.ItemsSource = entries;
        NoFilesText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesList.SelectedItem =
            entries.FirstOrDefault(entry => string.Equals(entry.File.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(entry => entry.File.Source == DiagnosticSource.GameCrash && !entry.File.IsBinary)
            ?? entries.FirstOrDefault();

        int crashes = entries.Count(entry => entry.File.Source == DiagnosticSource.GameCrash);
        StatusText.Text =
            $"{entries.Count} file(s) · {crashes} crash file(s) · Manager data: {_paths.Root}" +
            (_state.GameDirectory is null ? " · No game folder selected" : $" · Game: {_state.GameDirectory}");
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (FilesList.SelectedItem is not LogEntry entry)
        {
            ContentText.Text = "Select a log file to view it.";
            return;
        }

        if (entry.File.IsBinary)
        {
            ContentText.Text =
                $"{entry.File.FullPath}{Environment.NewLine}{Environment.NewLine}" +
                $"Binary crash dump ({entry.File.Size / 1024.0:N0} KB). It is included in Create Report and " +
                "can be opened with WinDbg or Visual Studio together with the matching game PDB.";
            return;
        }

        try
        {
            ContentText.Text = DiagnosticFiles.ReadTail(entry.File.FullPath);
            ContentText.ScrollToEnd();
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            ContentText.Text = $"The file could not be read: {exception.Message}";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Reload(_state);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        string target = FilesList.SelectedItem is LogEntry entry ? entry.File.FullPath : _paths.LogsDirectory;
        string arguments = System.IO.File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (string.IsNullOrEmpty(ContentText.Text)) return;
        Clipboard.SetText(ContentText.Text);
        StatusText.Text = "Log text copied to the clipboard.";
    }

    private async void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_busy) return;
        _busy = true;
        ReportButton.IsEnabled = false;
        StatusText.Text = "Creating diagnostic report…";
        try
        {
            string report = await Task.Run(() =>
                DiagnosticFiles.CreateReportAsync(_paths, _state, App.ManagerVersion));
            StatusText.Text = $"Report created: {report}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{report}\"") { UseShellExecute = true });
            Reload(_state);
        }
        catch (Exception exception)
        {
            ManagerLog.Error("Creating the diagnostic report failed.", exception);
            StatusText.Text = $"The report could not be created: {exception.Message}";
        }
        finally
        {
            _busy = false;
            ReportButton.IsEnabled = true;
        }
    }

    private sealed class LogEntry(DiagnosticFile file)
    {
        public DiagnosticFile File { get; } = file;
        public string Name => File.DisplayName;

        public string Details =>
            $"{SourceLabel} · {File.LastWriteTime:yyyy-MM-dd HH:mm:ss} · {FormatSize(File.Size)}";

        public Brush DetailBrush => File.Source == DiagnosticSource.GameCrash ? CrashBrush : MutedBrush;

        private string SourceLabel => File.Source switch
        {
            DiagnosticSource.Manager => "Manager",
            DiagnosticSource.GameCrash => File.IsBinary ? "Game crash dump" : "Game crash",
            DiagnosticSource.ManagerState => "Installed mod state",
            _ => "Game"
        };

        private static string FormatSize(long bytes) =>
            bytes < 1024 ? $"{bytes} B" :
            bytes < 1024 * 1024 ? $"{bytes / 1024.0:N1} KB" :
            $"{bytes / (1024.0 * 1024.0):N1} MB";
    }
}
