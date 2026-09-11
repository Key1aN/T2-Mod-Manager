using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using T2ModManager.Core.Game;
using T2ModManager.Core.Models;

namespace T2ModManager.App;

public partial class MainWindow : Window
{
    private readonly ModManagerService _service = new();
    private InstalledState _state = new();
    private string? _selectedGameDirectory;
    private string? _selectedModId;
    private bool _initialStateLoaded;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _state = await _service.GetStateAsync();
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
            UpdateInterface();
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        string? selectedGameDirectory = _selectedGameDirectory;
        if (selectedGameDirectory is null) return;
        await RunOperationAsync(progress =>
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
        await RunOperationAsync(async unusedProgress =>
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
        await RunOperationAsync(progress => activate
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
        await RunOperationAsync(progress => _service.RestoreVanillaAsync(progress));
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
