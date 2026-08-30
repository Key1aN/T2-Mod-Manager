using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using T2ModManager.Core.Game;
using T2ModManager.Core.Models;

namespace T2ModManager.App;

public partial class MainWindow : Window
{
    private readonly ModManagerService _service = new();
    private InstalledState _state = new();
    private string? _selectedGameDirectory;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _state = await _service.GetStateAsync();
        _selectedGameDirectory = _state.GameDirectory ?? _selectedGameDirectory;
        GamePathText.Text = _selectedGameDirectory ?? "No game folder selected";
        bool initialized = !string.IsNullOrWhiteSpace(_state.GameDirectory);
        CompatibilityExecutableState? compatibility = _state.CompatibilityExecutable;
        DiscFixStatusText.Text = compatibility is null
            ? "Not imported"
            : compatibility.Enabled ? "Active baseline" : "Inactive";
        DiscFixStatusBadge.Background = compatibility?.Enabled == true
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 112, 79))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 59, 74));
        DiscFixButton.Content = compatibility is null
            ? "Import & Enable Fix EXE"
            : compatibility.Enabled ? "Disable Fix" : "Enable Fix";
        DiscFixButton.IsEnabled = !_isBusy && initialized;
        InstalledMod? mod = _state.Mods.FirstOrDefault(item => item.Id == "Ultimate_Slashuur");
        ModStatusText.Text = mod is null ? "Not installed" : mod.Active ? "Active" : "Inactive";
        ModStatusBadge.Background = mod?.Active == true
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 112, 79))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 59, 74));
        ToggleModButton.Content = mod?.Active == true ? "Deactivate" : "Activate";
        ToggleModButton.IsEnabled = !_isBusy && initialized && mod is not null;
        InstallPackageButton.IsEnabled = !_isBusy && initialized;
        RestoreButton.IsEnabled = !_isBusy && initialized;
        LaunchButton.IsEnabled = !_isBusy && initialized;
        VerifyButton.IsEnabled = !_isBusy && _selectedGameDirectory is not null;
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
            GamePathText.Text = dialog.FolderName;
            VerifyButton.IsEnabled = true;
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGameDirectory is null) return;
        await RunOperationAsync(progress =>
            _service.InitializeGameAsync(_selectedGameDirectory, progress));
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
        await RunOperationAsync(async unusedProgress =>
        {
            _ = unusedProgress;
            await _service.InstallPackageAsync(dialog.FileName);
        });
    }

    private async void ToggleModButton_Click(object sender, RoutedEventArgs e)
    {
        InstalledMod? mod = _state.Mods.FirstOrDefault(item => item.Id == "Ultimate_Slashuur");
        if (mod is null) return;
        await RunOperationAsync(progress => mod.Active
            ? _service.DeactivateAsync(mod.Id, progress)
            : _service.ActivateAsync(mod.Id, progress));
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Restore the untouched retail TMNT2.exe and vanilla TMNT.DAT? This deactivates all mods and the obsolete disc-check fix.",
            "Restore Retail Original",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;
        await RunOperationAsync(progress => _service.RestoreRetailOriginalAsync(progress));
    }

    private async void DiscFixButton_Click(object sender, RoutedEventArgs e)
    {
        CompatibilityExecutableState? compatibility = _state.CompatibilityExecutable;
        if (compatibility is null)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Select the supported US/NA obsolete disc-check fix EXE",
                Filter = "TMNT2 executable (*.exe)|*.exe",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            await RunOperationAsync(progress =>
                _service.ImportAndEnableCompatibilityExecutableAsync(dialog.FileName, progress));
            return;
        }

        await RunOperationAsync(progress => compatibility.Enabled
            ? _service.DisableCompatibilityExecutableAsync(progress)
            : _service.EnableCompatibilityExecutableAsync(progress));
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.GameDirectory is null) return;
        string executable = System.IO.Path.Combine(_state.GameDirectory, "TMNT2.exe");
        Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = _state.GameDirectory,
            UseShellExecute = true
        });
    }

    private async Task RunOperationAsync(Func<IProgress<OperationProgress>, Task> operation)
    {
        if (_isBusy) return;
        _isBusy = true;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        await RefreshAsync();
        System.Progress<OperationProgress> operationProgress = new(update =>
        {
            StatusText.Text = update.Message;
            if (update.Fraction is not null) Progress.Value = update.Fraction.Value;
        });
        try
        {
            await operation(operationProgress);
            StatusText.Text = "Completed successfully";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Operation stopped safely";
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
}
