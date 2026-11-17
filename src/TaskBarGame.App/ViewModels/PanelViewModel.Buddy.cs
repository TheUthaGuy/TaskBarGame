using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TaskBarGame.App.Interop;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>Which shortcut the Settings tab is waiting for you to press.</summary>
public enum HotkeyCapture { None, Panel, Strip }

/// <summary>
/// The taskbar-buddy parts of the panel: the Wardrobe tab, the "right now" rates, picking a
/// monitor, keyboard shortcuts, and save backups.
/// </summary>
public sealed partial class PanelViewModel
{
    private ImageSource? _wardrobePreview;
    private string _wornText = "";
    private string _ratesHeadline = "";
    private string _ratesDetail = "";
    private string _ratesSession = "";
    private HotkeyCapture _capturing;
    private string _hotkeyMessage = "";
    private string _backupMessage = "";

    // ---------------------------------------------------------------- Wardrobe tab

    public IReadOnlyList<WardrobeRow> HatRows { get; private set; } = Array.Empty<WardrobeRow>();
    public IReadOnlyList<WardrobeRow> OutfitRows { get; private set; } = Array.Empty<WardrobeRow>();
    public ImageSource? WardrobePreview { get => _wardrobePreview; private set => Set(ref _wardrobePreview, value); }
    public string WornText { get => _wornText; private set => Set(ref _wornText, value); }

    // ---------------------------------------------------------------- Farming tab: right now

    public string RatesHeadline { get => _ratesHeadline; private set => Set(ref _ratesHeadline, value); }
    public string RatesDetail { get => _ratesDetail; private set => Set(ref _ratesDetail, value); }
    public string RatesSession { get => _ratesSession; private set => Set(ref _ratesSession, value); }

    // ---------------------------------------------------------------- Settings: monitor

    public ObservableCollection<MonitorChoiceRow> MonitorChoices { get; } = new();
    public bool HasOneMonitor => MonitorChoices.Count <= 1;

    // ---------------------------------------------------------------- Settings: keyboard shortcuts

    public string PanelHotkeyText => Capturing == HotkeyCapture.Panel ? "Press the keys…" : ShortcutLabel(Settings.PanelHotkey);
    public string StripHotkeyText => Capturing == HotkeyCapture.Strip ? "Press the keys…" : ShortcutLabel(Settings.StripHotkey);
    public string HotkeyMessage { get => _hotkeyMessage; private set => Set(ref _hotkeyMessage, value); }
    public ICommand ChangePanelHotkeyCommand { get; private set; } = null!;
    public ICommand ChangeStripHotkeyCommand { get; private set; } = null!;
    public ICommand ClearPanelHotkeyCommand { get; private set; } = null!;
    public ICommand ClearStripHotkeyCommand { get; private set; } = null!;

    /// <summary>Waiting for you to press a new shortcut (the window sends key presses to <see cref="OnCaptureKey"/>).</summary>
    public HotkeyCapture Capturing
    {
        get => _capturing;
        private set
        {
            if (!Set(ref _capturing, value)) return;
            _host.HotkeysPaused = value != HotkeyCapture.None; // so pressing the current shortcut reaches us
            Raise(nameof(PanelHotkeyText));
            Raise(nameof(StripHotkeyText));
        }
    }

    // ---------------------------------------------------------------- Settings: save file & backups

    public ObservableCollection<BackupRow> Backups { get; } = new();
    public string BackupMessage { get => _backupMessage; private set => Set(ref _backupMessage, value); }
    public ICommand BackupNowCommand { get; private set; } = null!;
    public ICommand ExportSaveCommand { get; private set; } = null!;
    public ICommand ImportSaveCommand { get; private set; } = null!;
    public ICommand OpenBackupsFolderCommand { get; private set; } = null!;

    private void InitBuddy()
    {
        HatRows = Cosmetics.Hats.Select(c => new WardrobeRow(_host, c, Wear)).ToList();
        OutfitRows = Cosmetics.Outfits.Select(c => new WardrobeRow(_host, c, Wear)).ToList();

        ChangePanelHotkeyCommand = new RelayCommand(() => StartCapture(HotkeyCapture.Panel));
        ChangeStripHotkeyCommand = new RelayCommand(() => StartCapture(HotkeyCapture.Strip));
        ClearPanelHotkeyCommand = new RelayCommand(() => SetHotkey(HotkeyCapture.Panel, ""));
        ClearStripHotkeyCommand = new RelayCommand(() => SetHotkey(HotkeyCapture.Strip, ""));

        BackupNowCommand = new RelayCommand(() =>
        {
            BackupMessage = _host.BackupNow("manual") ? "Backed up." : "Couldn't write the backup.";
            RefreshBackups();
        });
        ExportSaveCommand = new RelayCommand(ExportSave);
        ImportSaveCommand = new RelayCommand(ImportSave);
        OpenBackupsFolderCommand = new RelayCommand(() =>
        {
            try
            {
                Directory.CreateDirectory(_host.Saves.BackupFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_host.Saves.BackupFolder}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        });

        RefreshMonitors();
        RefreshBackups();
    }

    private void RefreshBuddy()
    {
        var hat = Cosmetics.WornHat(Engine);
        var outfit = Cosmetics.WornOutfit(Engine);
        WardrobePreview = _host.Sprites.Farmer("farmer_idle", -1, hat.Id, outfit.Id);
        FarmerIcon = WardrobePreview;
        WornText = $"Wearing the {hat.Name} and {outfit.Name}";
        foreach (var row in HatRows.Concat(OutfitRows)) row.Update(hat, outfit);

        var now = _host.Clock;
        var recent = _host.Rates.Recent(now);
        var session = _host.Rates.Session(now);
        if (!recent.IsMeasured)
        {
            RatesHeadline = "Measuring…";
            RatesDetail = "Give it a minute of farming to see how fast your farm is going.";
            RatesSession = "";
        }
        else
        {
            RatesHeadline = $"≈ {Format.Number((long)recent.CoinsPerHour)} coins an hour";
            RatesDetail = $"{Format.Number((long)recent.CropsPerHour)} crops and {Format.Number((long)recent.XpPerHour)} Farming XP an hour, "
                          + $"over the last {Format.Duration(recent.Seconds)}. Coins are what your harvest is worth at the Market.";
            RatesSession = session.Seconds > recent.Seconds + 30
                ? $"Since the game started ({Format.Duration(session.Seconds)} ago): ≈ {Format.Number((long)session.CoinsPerHour)} coins an hour"
                : "";
        }

        string errors = string.Join(" ", new[] { _host.PanelHotkeyError, _host.StripHotkeyError }.Where(e => e.Length > 0));
        if (Capturing == HotkeyCapture.None)
            HotkeyMessage = errors.Length > 0 ? errors : "They work from any app. Click one to change it.";
    }

    private void Wear(Cosmetic cosmetic)
    {
        if (!cosmetic.IsUnlocked(Engine)) return;
        if (cosmetic.Slot == CosmeticSlot.Hat) Settings.Hat = cosmetic.Id;
        else Settings.Outfit = cosmetic.Id;
        _host.Save();
        Refresh();
    }

    // ---------------------------------------------------------------- monitors

    public void RefreshMonitors()
    {
        var monitors = NativeMethods.Monitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        MonitorChoices.Clear();
        int number = 1;
        foreach (var monitor in monitors)
        {
            string where = "";
            if (!monitor.IsPrimary && primary is not null)
            {
                where = monitor.Bounds.X >= primary.Bounds.X + primary.Bounds.Width ? ", on the right"
                    : monitor.Bounds.X + monitor.Bounds.Width <= primary.Bounds.X ? ", on the left"
                    : monitor.Bounds.Y < primary.Bounds.Y ? ", above" : ", below";
            }
            string label = monitor.IsPrimary
                ? $"Main monitor ({monitor.Bounds.Width} × {monitor.Bounds.Height})"
                : $"Monitor {++number} ({monitor.Bounds.Width} × {monitor.Bounds.Height}{where})";
            bool chosen = Settings.Monitor == monitor.DeviceName
                          || (monitor.IsPrimary && monitors.All(m => m.DeviceName != Settings.Monitor));
            MonitorChoices.Add(new MonitorChoiceRow(label, chosen, () =>
                ChangeSetting(() => Settings.Monitor = monitor.IsPrimary ? null : monitor.DeviceName)));
        }
        Raise(nameof(HasOneMonitor));
    }

    // ---------------------------------------------------------------- keyboard shortcuts

    private static string ShortcutLabel(string text) => Hotkey.Parse(text)?.ToString() ?? "None";

    private void StartCapture(HotkeyCapture which)
    {
        Capturing = which;
        HotkeyMessage = "Press the keys you want, using Ctrl, Alt or Win (for example Ctrl+Alt+F). Backspace for none, Esc to cancel.";
    }

    /// <summary>A key was pressed while waiting for a new shortcut. Returns true if it was used.</summary>
    public bool OnCaptureKey(Key key, ModifierKeys modifiers)
    {
        if (Capturing == HotkeyCapture.None) return false;
        switch (key)
        {
            case Key.Escape:
                Capturing = HotkeyCapture.None;
                return true;
            case Key.Back or Key.Delete when modifiers == ModifierKeys.None:
                SetHotkey(Capturing, "");
                return true;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
            return true; // still holding modifiers: wait for the real key

        if (!Hotkey.IsUsable(modifiers, key))
        {
            HotkeyMessage = "Use Ctrl, Alt or Win together with another key (for example Ctrl+Alt+F).";
            return true;
        }
        SetHotkey(Capturing, new Hotkey(modifiers, key).ToString());
        return true;
    }

    private void SetHotkey(HotkeyCapture which, string text)
    {
        string other = which == HotkeyCapture.Panel ? Settings.StripHotkey : Settings.PanelHotkey;
        if (text.Length > 0 && string.Equals(text, other, StringComparison.OrdinalIgnoreCase))
        {
            HotkeyMessage = $"{text} is already the other shortcut. Pick a different one.";
            return;
        }
        if (which == HotkeyCapture.Panel) Settings.PanelHotkey = text;
        else Settings.StripHotkey = text;
        Capturing = HotkeyCapture.None;
        _host.ApplySettings(); // saves and registers the new shortcut
        Raise(nameof(PanelHotkeyText));
        Raise(nameof(StripHotkeyText));
        RefreshBuddy();
    }

    // ---------------------------------------------------------------- backups, export & import

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var backup in _host.Saves.ListBackups())
        {
            var path = backup.Path;
            string kind = Path.GetFileNameWithoutExtension(path) switch
            {
                var n when n.EndsWith("-manual") => " · made by you",
                var n when n.Contains("-before-") => " · from just before loading another save",
                _ => "",
            };
            Backups.Add(new BackupRow($"{backup.Time:ddd d MMM, HH:mm}{kind}", backup.Summary, new RelayCommand(() =>
            {
                if (SaveManager.Read(path) is { } state) LoadSave(state, $"the backup from {backup.Time:ddd d MMM, HH:mm}", "restore");
                else BackupMessage = "That backup can't be read.";
            })));
        }
    }

    private void ExportSave()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export your farm",
            FileName = $"TaskBarGame Harvest Edition save {DateTime.Now:yyyy-MM-dd}.json",
            Filter = "Save files (*.json)|*.json",
            DefaultExt = ".json",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _host.Save();
            _host.Saves.Export(Engine.State, dialog.FileName);
            BackupMessage = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupMessage = $"Couldn't export: {ex.Message}";
        }
    }

    private void ImportSave()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a farm",
            Filter = "Save files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        if (SaveManager.Read(dialog.FileName) is not { } state)
        {
            BackupMessage = "That file isn't a Harvest Edition save, so nothing was changed.";
            return;
        }
        LoadSave(state, Path.GetFileName(dialog.FileName), "import");
    }

    /// <summary>Backs up the current farm, puts <paramref name="state"/> in its place and restarts the game to play it.</summary>
    private void LoadSave(GameState state, string what, string label)
    {
        var answer = MessageBox.Show(
            $"Load {what}?\n\n{SaveManager.Summarize(state)}\n\nYour current farm ({SaveManager.Summarize(Engine.State)}) is backed up first, "
            + "so you can come back to it from the backups list. The game restarts to load it.",
            "Load a save", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (!_host.BackupNow("before-" + label))
        {
            BackupMessage = "Couldn't back up your current farm, so nothing was changed.";
            return;
        }
        try
        {
            _host.Saves.Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackupMessage = $"Couldn't write the save: {ex.Message}";
            return;
        }
        ((App)Application.Current).RestartToLoadSave();
    }
}

/// <summary>A hat or outfit in the Wardrobe tab.</summary>
public sealed class WardrobeRow : ObservableObject
{
    private readonly GameHost _host;
    private ImageSource? _preview;
    private string _status = "";
    private bool _isUnlocked;
    private bool _isWorn;

    public WardrobeRow(GameHost host, Cosmetic cosmetic, Action<Cosmetic> wear)
    {
        _host = host;
        Cosmetic = cosmetic;
        WearCommand = new RelayCommand(() => wear(cosmetic));
    }

    public Cosmetic Cosmetic { get; }
    public string Name => Cosmetic.Name;
    public string Milestone => Cosmetic.Milestone;
    public ICommand WearCommand { get; }
    public ImageSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsUnlocked { get => _isUnlocked; private set => Set(ref _isUnlocked, value); }
    public bool IsWorn { get => _isWorn; private set => Set(ref _isWorn, value); }

    public void Update(Cosmetic hat, Cosmetic outfit)
    {
        var engine = _host.Engine;
        IsUnlocked = Cosmetic.IsUnlocked(engine);
        IsWorn = Cosmetic == hat || Cosmetic == outfit;
        // Show it on the farmer with the rest of what they're wearing.
        Preview = Cosmetic.Slot == CosmeticSlot.Hat
            ? _host.Sprites.Farmer("farmer_idle", -1, Cosmetic.Id, outfit.Id)
            : _host.Sprites.Farmer("farmer_idle", -1, hat.Id, Cosmetic.Id);

        var (have, need) = Cosmetic.Progress(engine);
        Status = IsWorn ? "Wearing it"
            : IsUnlocked ? "Unlocked"
            : need > 1 ? $"{Format.Number(Math.Min(have, need))} / {Format.Number(need)}"
            : "Locked";
    }
}

/// <summary>A monitor to put the strip on.</summary>
public sealed class MonitorChoiceRow(string label, bool isChosen, Action choose) : ObservableObject
{
    private bool _isChosen = isChosen;

    public string Label { get; } = label;

    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (!Set(ref _isChosen, value) || !value) return;
            choose();
        }
    }
}

/// <summary>A backup in the Settings tab.</summary>
public sealed record BackupRow(string When, string Summary, ICommand RestoreCommand);
