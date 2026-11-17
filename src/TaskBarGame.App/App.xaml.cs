using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TaskBarGame.Core;

namespace TaskBarGame.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private GameHost? _host;
    private bool _shownCrashMessage;
    private bool _quitting;
    private bool _screenshotMode;

    /// <summary>Errors are written here so they can be reported and fixed.</summary>
    public static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskBarGame", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        if (ScreenshotTour.OutputFolder(e.Args) is string screenshotFolder)
        {
            StartScreenshotTour(screenshotFolder);
            return;
        }

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

    private static void LogError(Exception ex, string? extra = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}"
                + (extra is null ? "" : extra + Environment.NewLine) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// "TaskBarGame.exe --screenshots folder" plays a demo garden, saves screenshots and quits.
    /// GitHub Actions uses this to keep the screenshots in the README up to date.
    /// </summary>
    private void StartScreenshotTour(string folder)
    {
        _screenshotMode = true;
        var saves = new SaveManager(Path.Combine(Path.GetTempPath(), "TaskBarGame-screenshots", "save.json"));
        _host = new GameHost(saves, ScreenshotTour.CreateDemoState());

        var strip = new StripWindow(_host);
        MainWindow = strip;
        strip.Show();
        _host.Start();

        _ = RunTour();

        async Task RunTour()
        {
            try
            {
                await new ScreenshotTour(_host, strip, folder).RunAsync();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                LogError(ex);
                Shutdown(1);
            }
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // The exception's own stack stops at the dispatcher; this shows what led there.
        LogError(e.Exception, "Called from:" + Environment.NewLine + Environment.StackTrace);
        if (_screenshotMode)
        {
            // No one is there to click a message box: fail the run instead.
            e.Handled = true;
            Shutdown(1);
            return;
        }

        // Keep the game running: one broken animation shouldn't lose your farm.
        e.Handled = true;
        if (_shownCrashMessage) return;
        _shownCrashMessage = true;
        MessageBox.Show(
            $"Something went wrong, but your progress is safe.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{ErrorLogPath}",
            "TaskBarGame", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
