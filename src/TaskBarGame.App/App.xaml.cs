using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TaskBarGame.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private GameHost? _host;
    private bool _shownCrashMessage;
    private bool _quitting;

    /// <summary>Errors are written here so they can be reported and fixed.</summary>
    public static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskBarGame", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TaskBarGame.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("TaskBarGame is already running. Look just above your taskbar!",
                "TaskBarGame", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        try
        {
            _host = new GameHost();
            var offline = _host.CatchUpOfflineTime();

            var strip = new StripWindow(_host);
            MainWindow = strip;
            strip.Closed += (_, _) => Quit();
            strip.Show();
            _host.Start();

            if (!offline.IsEmpty && offline.Away >= TimeSpan.FromMinutes(1))
                strip.ShowOfflineReport(offline);
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show($"TaskBarGame couldn't start.\n\n{ex.Message}\n\nDetails were saved to:\n{ErrorLogPath}",
                "TaskBarGame", MessageBoxButton.OK, MessageBoxImage.Error);
            _host = null; // don't save a half-started game over a good save file
            Shutdown();
        }
    }

    /// <summary>Saves and closes the game.</summary>
    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _host?.Save();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is shutting down or logging off.
        _host?.Save();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Save();
        _host?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);

        // Keep the game running: one broken animation shouldn't lose your farm.
        e.Handled = true;
        if (_shownCrashMessage) return;
        _shownCrashMessage = true;
        MessageBox.Show(
            $"Something went wrong, but your progress is safe.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{ErrorLogPath}",
            "TaskBarGame", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
