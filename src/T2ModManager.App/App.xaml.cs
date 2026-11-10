using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using T2ModManager.Core.Diagnostics;
using T2ModManager.Core.State;

namespace T2ModManager.App;

public partial class App : Application
{
    public static string ManagerVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            ManagerLog.Initialize(new AppDataPaths().LogsDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.IO.IOException or ArgumentException)
        {
            // The main window reports an unwritable portable folder to the user.
        }

        ManagerLog.Info(
            $"T2 Mod Manager {ManagerVersion} started from \"{Environment.ProcessPath}\" " +
            $"(data: \"{new AppDataPaths().Root}\", OS: {Environment.OSVersion}).");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ManagerLog.Error("Unhandled exception (manager is closing).", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ManagerLog.Error("Unobserved background task exception.", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ManagerLog.Info($"T2 Mod Manager closed (exit code {e.ApplicationExitCode}).");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _ = sender;
        ManagerLog.Error("Unexpected manager error.", e.Exception);
        MessageBox.Show(
            $"{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            "The details were written to the log. Open Logs and use Create Report to share them.",
            "T2 Mod Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
