using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>Passed to the new version when the game restarts after updating itself.</summary>
    private const string UpdatedArgument = "--updated";

    /// <summary>Passed to the game when it restarts to load a restored or imported save.</summary>
    private const string RestartedArgument = "--restarted";

    /// <summary>Errors are written here so they can be reported and fixed.</summary>
    /// <summary>The name shown in message boxes and window titles.</summary>
    public const string Title = "TaskBarGame: Harvest Edition";

    public static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SaveManager.FolderName, "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        if (ScreenshotTour.OutputFolder(e.Args) is string screenshotFolder)
        {
            StartScreenshotTour(screenshotFolder);
            return;
        }

        bool justUpdated = e.Args.Contains(UpdatedArgument);
        bool restarted = justUpdated || e.Args.Contains(RestartedArgument);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TaskBarGame.HarvestEdition.SingleInstance", out bool isFirst);
        if (!isFirst && restarted)
        {
            // The old version is still closing: wait for it.
            try { isFirst = _singleInstance.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        if (!isFirst)
        {
            MessageBox.Show("The Harvest Edition is already running. Look just above your taskbar!",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
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
            _host.StartBackups();
            StartUpdater(_host);

            if (justUpdated) strip.ShowUpdated(Updater.CurrentVersion);
            if (!offline.IsEmpty && offline.Away >= TimeSpan.FromMinutes(1))
                strip.ShowOfflineReport(offline);
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show($"{Title} couldn't start.\n\n{ex.Message}\n\nDetails were saved to:\n{ErrorLogPath}",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            _host = null; // don't save a half-started game over a good save file
            Shutdown();
        }
    }

    /// <summary>A problem with updates must never stop the game from starting.</summary>
    private static void StartUpdater(GameHost host)
    {
        try
        {
            host.Updater.Start();
        }
        catch (Exception ex)
        {
            LogError(ex, "Updates are off until the game restarts.");
        }
    }

    /// <summary>Downloads and installs the new version, then restarts into it (the "Update now" button).</summary>
    public async void UpdateNow()
    {
        if (_host is null) return;
        if (await _host.Updater.InstallAsync()) RestartIntoNewVersion();
    }

    /// <summary>Saves, then starts the newly installed version and closes this one.</summary>
    public void RestartIntoNewVersion()
    {
        if (_host is null || Environment.ProcessPath is not { } exe) return;
        _host.Save();
        _quitting = true;

        // Let the new version take the "only one copy running" lock straight away.
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _singleInstance = null;

        try
        {
            Process.Start(new ProcessStartInfo(exe, UpdatedArgument) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            LogError(ex);
            MessageBox.Show($"The update is installed, but the game couldn't restart by itself. Start it again to play the new version.\n\n{ex.Message}",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Shutdown();
    }

    /// <summary>
    /// Restarts the game without saving, so it loads a save file that was just put in place
    /// (restoring a backup or importing a save).
    /// </summary>
    public void RestartToLoadSave()
    {
        if (Environment.ProcessPath is not { } exe) return;
        _quitting = true;
        _host?.Dispose();
        _host = null; // don't save over the save that was just put in place

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _singleInstance = null;
        try
        {
            Process.Start(new ProcessStartInfo(exe, RestartedArgument) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            LogError(ex);
            MessageBox.Show($"The save is in place, but the game couldn't restart by itself. Start it again to play it.\n\n{ex.Message}",
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Shutdown();
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
    /// "TaskBarGame.exe --screenshots folder" plays a demo field, saves screenshots and quits.
    /// GitHub Actions uses this to keep the screenshots in the README up to date.
    /// </summary>
    private void StartScreenshotTour(string folder)
    {
        _screenshotMode = true;
        var saves = new SaveManager(Path.Combine(Path.GetTempPath(), "TaskBarGame-HarvestEdition-screenshots", "save.json"));
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
            Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
