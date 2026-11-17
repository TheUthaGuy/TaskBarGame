using System;
using System.Windows.Input;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>The Updates card in the Settings tab.</summary>
public sealed partial class PanelViewModel
{
    private string _updateStatus = "";
    private bool _canUpdateNow;
    private bool _canRestartForUpdate;
    private bool _hasReleasePage;
    private bool _isUpdateBusy;

    private Updater Updater => _host.Updater;

    public string VersionText => $"You have version {Updater.CurrentVersion}";
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }
    public bool CanUpdateNow { get => _canUpdateNow; private set => Set(ref _canUpdateNow, value); }
    public bool CanRestartForUpdate { get => _canRestartForUpdate; private set => Set(ref _canRestartForUpdate, value); }
    public bool HasReleasePage { get => _hasReleasePage; private set => Set(ref _hasReleasePage, value); }

    /// <summary>Checking or downloading: the Check now button waits.</summary>
    public bool IsUpdateBusy { get => _isUpdateBusy; private set => Set(ref _isUpdateBusy, value); }

    public ICommand UpdateNowCommand { get; private set; } = null!;
    public ICommand RestartForUpdateCommand { get; private set; } = null!;
    public ICommand OpenReleasePageCommand { get; private set; } = null!;

    public bool CheckForUpdates
    {
        get => Settings.CheckForUpdates;
        set => ChangeSetting(() => Settings.CheckForUpdates = value);
    }

    public bool InstallUpdatesAutomatically
    {
        get => Settings.InstallUpdatesAutomatically;
        set => ChangeSetting(() => Settings.InstallUpdatesAutomatically = value);
    }

    private void InitUpdates()
    {
        CheckNowCommand = new RelayCommand(async () =>
        {
            await Updater.CheckAsync(manual: true);
            RefreshUpdates();
        });
        UpdateNowCommand = new RelayCommand(() => ((App)System.Windows.Application.Current).UpdateNow());
        RestartForUpdateCommand = new RelayCommand(() => ((App)System.Windows.Application.Current).RestartIntoNewVersion());
        OpenReleasePageCommand = new RelayCommand(() => StripWindow.OpenWebPage(Updater.Release?.PageUrl ?? Updates.ReleasesPageUrl));
    }

    public ICommand CheckNowCommand { get; private set; } = null!;

    private void RefreshUpdates()
    {
        var release = Updater.Release;
        string checkedAt = Updater.LastChecked is { } at ? $" (checked at {at:t})" : "";
        UpdateStatus = Updater.State switch
        {
            UpdateState.Checking => "Checking for a new version…",
            UpdateState.UpToDate => $"You have the newest version{checkedAt}.",
            UpdateState.Available when release is not null =>
                $"Version {release.Version} is out!" + (Updater.CanInstall ? "" : " This copy can't update itself: download it from the release page."),
            UpdateState.Downloading => $"Downloading version {release?.Version}… {Updater.Progress * 100:0}%",
            UpdateState.Installed => $"Version {release?.Version} is installed. Restart the game to start playing it.",
            UpdateState.Failed => Updater.Error,
            _ => Settings.CheckForUpdates ? "The game checks for new versions a little after it starts, then every 6 hours." : "Automatic checks are off.",
        };
        IsUpdateBusy = Updater.State is UpdateState.Checking or UpdateState.Downloading;
        CanUpdateNow = release is not null && Updater.CanInstall && Updater.State is UpdateState.Available or UpdateState.Failed;
        CanRestartForUpdate = Updater.State == UpdateState.Installed;
        HasReleasePage = release is not null;
    }
}
