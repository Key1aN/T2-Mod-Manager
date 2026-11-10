using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using T2ModManager.Core.Diagnostics;
using T2ModManager.Core.Game;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.State;

namespace T2ModManager.App;

public partial class MainWindow : Window
{
    private ModManagerService _service = null!;
    private bool _serviceReady;
    private LogsWindow? _logsWindow;
    private InstalledState _state = new();
    private string? _selectedGameDirectory;
    private string? _selectedModId;
    private bool _initialStateLoaded;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            _service = new ModManagerService();
            _serviceReady = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.IO.IOException)
        {
            ManagerLog.Error("The portable data folder could not be prepared.", exception);
            StatusText.Text = "The manager folder is not writable";
            UpdateInterface();
            MessageBox.Show(this, exception.Message, "T2 Mod Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _isBusy = true;
        UpdateInterface();
        try
        {
            System.Progress<OperationProgress> progress = new(update => StatusText.Text = update.Message);
            PortableMigrationResult migration = await _service.MigrateLegacyDataAsync(progress);
            if (migration.Outcome == PortableMigrationOutcome.Migrated)
            {
                StatusText.Text = "Previous manager data copied into the portable folder";
                MessageBox.Show(
                    this,
                    "T2 Mod Manager is now portable. Your previous mods, settings, and verified vanilla " +
                    $"backup were copied from{Environment.NewLine}{migration.LegacyRoot}{Environment.NewLine}" +
                    $"to{Environment.NewLine}{_service.Paths.Root}{Environment.NewLine}{Environment.NewLine}" +
                    "The old folder is no longer used and was left untouched. You can delete it after " +
                    "confirming that your mods appear here.",
                    "Portable data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            ManagerLog.Error("Copying previous manager data into the portable folder failed.", exception);
            MessageBox.Show(
                this,
                $"Previous manager data could not be copied into the portable folder:{Environment.NewLine}" +
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                "Nothing in the old folder was changed. Details are in Logs.",
                "T2 Mod Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _isBusy = false;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!_serviceReady) return;
        try
        {
            _state = await _service.GetStateAsync();
        }
        catch (Exception exception)
        {
            ManagerLog.Error("Loading the manager state failed.", exception);
            StatusText.Text = "Manager state could not be loaded; see Logs";
        }

        if (!_initialStateLoaded)
        {
            _selectedGameDirectory = GameDirectoryLocator.ContainsRequiredFiles(_state.GameDirectory)
                ? GameDirectoryLocator.Normalize(_state.GameDirectory!)
                : null;
            _initialStateLoaded = true;
        }

        UpdateInterface();
    }

    private void UpdateInterface()
    {
        GamePathText.Text = _selectedGameDirectory ?? "No game folder selected";
        bool initialized =
            GameDirectoryLocator.ContainsRequiredFiles(_state.GameDirectory) &&
            GameDirectoryLocator.PathsEqual(_state.GameDirectory, _selectedGameDirectory);
        List<ModListEntry> entries = _state.Mods
            .OrderBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(mod => new ModListEntry(mod, !_isBusy && initialized))
            .ToList();
        ModsList.ItemsSource = entries;
        NoModsText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ModListEntry? selectedEntry = entries.FirstOrDefault(entry =>
            entry.Id.Equals(_selectedModId, StringComparison.OrdinalIgnoreCase));
        ModsList.SelectedItem = selectedEntry;
        if (selectedEntry is null)
        {
            _selectedModId = null;
            ShowNoModSelected();
        }
        else
        {
            ShowModDescription(selectedEntry.Mod);
        }

        initialized &= _serviceReady;
        InstallPackageButton.IsEnabled = !_isBusy && initialized;
        RestoreButton.IsEnabled = !_isBusy && initialized;
        LaunchButton.IsEnabled = !_isBusy && initialized;
        VerifyButton.IsEnabled = !_isBusy && _serviceReady && _selectedGameDirectory is not null;
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Select the TMNT 2: Battle Nexus game folder",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _selectedGameDirectory = dialog.FolderName;
            UpdateInterface();
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        string? selectedGameDirectory = _selectedGameDirectory;
        if (selectedGameDirectory is null) return;
        await RunOperationAsync($"Verify and back up \"{selectedGameDirectory}\"", progress =>
            _service.InitializeGameAsync(selectedGameDirectory, progress));
    }

    private async void InstallPackageButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Add a TMNT2 mod package",
            Filter = "TMNT2 mod package (*.t2mod)|*.t2mod",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunOperationAsync($"Add package \"{dialog.FileName}\"", async unusedProgress =>
        {
            _ = unusedProgress;
            InstalledMod installed = await _service.InstallPackageAsync(dialog.FileName);
            _selectedModId = installed.Id;
        });
    }

    private void ModsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ModsList.SelectedItem is not ModListEntry entry)
        {
            ShowNoModSelected();
            return;
        }

        _selectedModId = entry.Id;
        ShowModDescription(entry.Mod);
    }

    private async void ModActiveCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not CheckBox checkBox || checkBox.DataContext is not ModListEntry entry)
        {
            return;
        }

        _selectedModId = entry.Id;
        bool activate = checkBox.IsChecked == true;
        await RunOperationAsync($"{(activate ? "Activate" : "Deactivate")} mod {entry.Id} {entry.Mod.Version}", progress => activate
            ? _service.ActivateAsync(entry.Id, progress)
            : _service.DeactivateAsync(entry.Id, progress));
    }

    private void ShowModDescription(InstalledMod mod)
    {
        SelectedModNameText.Text = mod.DisplayName;
        SelectedModVersionText.Text =
            $"Version {mod.Version} · Package ID: {mod.Id} · {(mod.Active ? "Active" : "Inactive")}";
        SelectedModDescriptionText.Text = string.IsNullOrWhiteSpace(mod.Description)
            ? "No description was provided by this .t2mod package."
            : mod.Description;
    }

    private void ShowNoModSelected()
    {
        SelectedModNameText.Text = "No mod selected";
        SelectedModVersionText.Text = string.Empty;
        SelectedModDescriptionText.Text = _state.Mods.Count == 0
            ? "Add a .t2mod package to populate the mod list."
            : "Select a mod from the list to view the description stored in its .t2mod package.";
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Restore the verified vanilla TMNT2.exe and TMNT.DAT and deactivate all mods?",
            "Restore Vanilla",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;
        await RunOperationAsync("Restore vanilla", progress => _service.RestoreVanillaAsync(progress));
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_state.GameDirectory is null) return;
        string gameDirectory = _state.GameDirectory;
        string executable = System.IO.Path.Combine(gameDirectory, "TMNT2.exe");
        string activeMods = string.Join(", ", _state.Mods.Where(mod => mod.Active).Select(mod => $"{mod.Id} {mod.Version}"));
        try
        {
            string hash = await Sha256Service.HashFileAsync(executable);
            ManagerLog.Info(
                $"Launching game \"{executable}\" (SHA-256 {hash}); active mods: " +
                $"{(activeMods.Length == 0 ? "none" : activeMods)}.");
            DateTime launchedAt = DateTime.Now;
            Process? process = Process.Start(new ProcessStartInfo(executable)
            {
                WorkingDirectory = gameDirectory,
                UseShellExecute = true
            });
            if (process is null)
            {
                ManagerLog.Warning("The game process handle was not returned; its exit cannot be monitored.");
                return;
            }

            StatusText.Text = "Game running";
            await MonitorGameAsync(process, gameDirectory, launchedAt);
        }
        catch (Exception exception)
        {
            ManagerLog.Error("Launching the game failed.", exception);
            MessageBox.Show(this, exception.Message, "T2 Mod Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task MonitorGameAsync(Process process, string gameDirectory, DateTime launchedAt)
    {
        using (process)
        {
            await process.WaitForExitAsync();
            int exitCode = process.ExitCode;
            string code = $"{exitCode} (0x{unchecked((uint)exitCode):X8})";
            List<DiagnosticFile> newCrashFiles = DiagnosticFiles.Find(_service.Paths, gameDirectory)
                .Where(file => file.Source == DiagnosticOrigin.GameCrash &&
                               file.LastWriteTime >= launchedAt.AddSeconds(-2))
                .ToList();
            if (exitCode == 0 && newCrashFiles.Count == 0)
            {
                ManagerLog.Info($"Game exited normally (exit code {code}).");
                StatusText.Text = "Game closed normally";
                return;
            }

            string crashList = newCrashFiles.Count == 0
                ? "no new crash files were written"
                : "new crash files: " + string.Join(", ", newCrashFiles.Select(file => file.DisplayName));
            ManagerLog.Error($"Game exited abnormally with exit code {code}; {crashList}.");
            StatusText.Text = $"Game exited abnormally (code 0x{unchecked((uint)exitCode):X8}); open Logs";
            OpenLogsWindow();
        }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenLogsWindow();
    }

    private void OpenLogsWindow()
    {
        AppDataPaths paths = _serviceReady ? _service.Paths : new AppDataPaths();
        if (_logsWindow is { IsLoaded: true })
        {
            _logsWindow.Reload(_state);
            _logsWindow.Activate();
            return;
        }

        _logsWindow = new LogsWindow(paths, _state) { Owner = this };
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Show();
    }

    private async Task RunOperationAsync(string name, Func<IProgress<OperationProgress>, Task> operation)
    {
        if (_isBusy) return;
        _isBusy = true;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        await RefreshAsync();
        ManagerLog.Info($"Operation started: {name}");
        System.Progress<OperationProgress> operationProgress = new(update =>
        {
            ManagerLog.Info($"  [{update.Stage}] {update.Message}");
            StatusText.Text = update.Message;
            if (update.Fraction is not null) Progress.Value = update.Fraction.Value;
        });
        try
        {
            await operation(operationProgress);
            ManagerLog.Info($"Operation completed: {name}");
            StatusText.Text = "Completed successfully";
        }
        catch (Exception exception)
        {
            ManagerLog.Error($"Operation stopped: {name}", exception);
            StatusText.Text = "Operation stopped safely; details are in Logs";
            MessageBox.Show(this, exception.Message, "T2 Mod Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            Progress.Visibility = Visibility.Collapsed;
            await RefreshAsync();
        }
    }

    private sealed class ModListEntry(InstalledMod mod, bool canToggle)
    {
        public InstalledMod Mod { get; } = mod;
        public string Id => Mod.Id;
        public string DisplayName => Mod.DisplayName;
        public string VersionLabel => $"Version {Mod.Version}";
        public bool Active => Mod.Active;
        public bool CanToggle { get; } = canToggle;
    }
}
