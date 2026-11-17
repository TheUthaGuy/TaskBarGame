using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>Everything the garden panel shows. <see cref="Refresh"/> pulls fresh numbers from the game.</summary>
public sealed class PanelViewModel : ObservableObject
{
    private static readonly string[] SettingProperties =
    {
        nameof(ModeAlways), nameof(ModeHover),
        nameof(SizeSmall), nameof(SizeNormal), nameof(SizeLarge),
        nameof(AnchorLeft), nameof(AnchorCenter), nameof(AnchorRight),
        nameof(AutoHarvest), nameof(AutoSell), nameof(FloatingText), nameof(HideInFullscreen),
        nameof(StartWithWindows),
    };

    private readonly GameHost _host;

    private string _coinsText = "";
    private string _gardenLevelText = "";
    private string _gardenXpText = "";
    private double _gardenProgress;
    private string _gardenNextText = "";
    private string _plotsHeader = "";
    private bool _hasNextPlot;
    private bool _canUnlockPlot;
    private string _nextPlotTitle = "";
    private string _nextPlotText = "";
    private string _farmingLevelText = "";
    private string _farmingXpText = "";
    private double _farmingProgress;
    private string _farmingNextText = "";
    private string _fortuneLevelText = "";
    private string _statsText = "";
    private string _inventoryValueText = "";
    private bool _hasInventory;

    public PanelViewModel(GameHost host)
    {
        _host = host;
        CoinIcon = host.Sprites["coin"];
        FarmerIcon = host.Sprites["farmer_idle"];
        LockIcon = host.Sprites["lock"];
        CropGuide = Crops.All.Select(c => new CropGuideRow(c, host.Sprites.CropIcon(c))).ToList();
        CollectionRows = Crops.All.Select(c => new CollectionRow(c, host.Sprites.CropIcon(c))).ToList();

        UnlockPlotCommand = new RelayCommand(() =>
        {
            if (Engine.TryUnlockNextPlot(out _)) _host.Save();
            Refresh();
        });
        SellEverythingCommand = new RelayCommand(() =>
        {
            Engine.SellAll();
            Refresh();
        });
        Refresh();
    }

    private GameEngine Engine => _host.Engine;
    private GameSettings Settings => _host.Settings;

    public ImageSource CoinIcon { get; }
    public ImageSource FarmerIcon { get; }
    public ImageSource LockIcon { get; }
    public string CoinsText { get => _coinsText; private set => Set(ref _coinsText, value); }

    // ---------------------------------------------------------------- Garden tab

    public string GardenLevelText { get => _gardenLevelText; private set => Set(ref _gardenLevelText, value); }
    public string GardenXpText { get => _gardenXpText; private set => Set(ref _gardenXpText, value); }
    public double GardenProgress { get => _gardenProgress; private set => Set(ref _gardenProgress, value); }
    public string GardenNextText { get => _gardenNextText; private set => Set(ref _gardenNextText, value); }
    public string PlotsHeader { get => _plotsHeader; private set => Set(ref _plotsHeader, value); }
    public ObservableCollection<PlotRow> Plots { get; } = new();
    public bool HasNextPlot { get => _hasNextPlot; private set => Set(ref _hasNextPlot, value); }
    public bool CanUnlockPlot { get => _canUnlockPlot; private set => Set(ref _canUnlockPlot, value); }
    public string NextPlotTitle { get => _nextPlotTitle; private set => Set(ref _nextPlotTitle, value); }
    public string NextPlotText { get => _nextPlotText; private set => Set(ref _nextPlotText, value); }
    public ICommand UnlockPlotCommand { get; }

    // ---------------------------------------------------------------- Farming tab

    public string FarmingLevelText { get => _farmingLevelText; private set => Set(ref _farmingLevelText, value); }
    public string FarmingXpText { get => _farmingXpText; private set => Set(ref _farmingXpText, value); }
    public double FarmingProgress { get => _farmingProgress; private set => Set(ref _farmingProgress, value); }
    public string FarmingNextText { get => _farmingNextText; private set => Set(ref _farmingNextText, value); }
    public string FortuneLevelText { get => _fortuneLevelText; private set => Set(ref _fortuneLevelText, value); }
    public string FortuneHandText => $"+{GameEngine.HandHarvestFortuneBonus} when you click a ripe crop yourself";
    public string FortuneCollectionText => $"+{CollectionTiers.FortunePerTier} per collection tier (for that crop only)";
    public string StatsText { get => _statsText; private set => Set(ref _statsText, value); }
    public IReadOnlyList<CropGuideRow> CropGuide { get; }

    // ---------------------------------------------------------------- Collections tab

    public IReadOnlyList<CollectionRow> CollectionRows { get; }

    // ---------------------------------------------------------------- Market tab

    public ObservableCollection<InventoryRow> Inventory { get; } = new();
    public string InventoryValueText { get => _inventoryValueText; private set => Set(ref _inventoryValueText, value); }
    public bool HasInventory { get => _hasInventory; private set => Set(ref _hasInventory, value); }
    public ICommand SellEverythingCommand { get; }

    // ---------------------------------------------------------------- Settings tab

    public bool ModeAlways
    {
        get => Settings.StripMode == StripMode.Always;
        set { if (value) ChangeSetting(() => Settings.StripMode = StripMode.Always); }
    }

    public bool ModeHover
    {
        get => Settings.StripMode == StripMode.OnHover;
        set { if (value) ChangeSetting(() => Settings.StripMode = StripMode.OnHover); }
    }

    public bool SizeSmall
    {
        get => Settings.Size == StripSize.Small;
        set { if (value) ChangeSetting(() => Settings.Size = StripSize.Small); }
    }

    public bool SizeNormal
    {
        get => Settings.Size == StripSize.Normal;
        set { if (value) ChangeSetting(() => Settings.Size = StripSize.Normal); }
    }

    public bool SizeLarge
    {
        get => Settings.Size == StripSize.Large;
        set { if (value) ChangeSetting(() => Settings.Size = StripSize.Large); }
    }

    public bool AnchorLeft
    {
        get => Settings.Anchor == StripAnchor.Left;
        set { if (value) ChangeSetting(() => Settings.Anchor = StripAnchor.Left); }
    }

    public bool AnchorCenter
    {
        get => Settings.Anchor == StripAnchor.Center;
        set { if (value) ChangeSetting(() => Settings.Anchor = StripAnchor.Center); }
    }

    public bool AnchorRight
    {
        get => Settings.Anchor == StripAnchor.Right;
        set { if (value) ChangeSetting(() => Settings.Anchor = StripAnchor.Right); }
    }

    public bool AutoHarvest
    {
        get => Settings.FarmerAutoHarvest;
        set => ChangeSetting(() => Settings.FarmerAutoHarvest = value);
    }

    public bool AutoSell
    {
        get => Settings.AutoSell;
        set => ChangeSetting(() => Settings.AutoSell = value);
    }

    public bool FloatingText
    {
        get => Settings.ShowFloatingText;
        set => ChangeSetting(() => Settings.ShowFloatingText = value);
    }

    public bool HideInFullscreen
    {
        get => Settings.HideInFullscreen;
        set => ChangeSetting(() => Settings.HideInFullscreen = value);
    }

    public bool StartWithWindows
    {
        get
        {
            try { return WindowsStartup.IsEnabled; }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
        set
        {
            try
            {
                WindowsStartup.IsEnabled = value;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                MessageBox.Show($"Windows didn't allow that change.\n\n{ex.Message}", "TaskBarGame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Raise();
        }
    }

    public string SavePath => _host.Saves.SavePath;

    /// <summary>Re-reads every setting, e.g. after it was changed from the strip's right-click menu.</summary>
    public void RaiseSettings()
    {
        foreach (var name in SettingProperties) Raise(name);
    }

    private void ChangeSetting(Action change)
    {
        change();
        _host.ApplySettings();
        RaiseSettings();
    }

    // ---------------------------------------------------------------- refresh

    public void Refresh()
    {
        var state = Engine.State;
        CoinsText = Format.Number(state.Coins);
        RefreshGarden();
        RefreshFarming();
        foreach (var row in CollectionRows) row.Update(Engine);
        RefreshMarket();
    }

    private void RefreshGarden()
    {
        int level = Engine.GardenLevel;
        long xp = Engine.State.GardenXp;
        GardenLevelText = $"Garden Level {level}";

        if (level >= GardenProgression.MaxLevel)
        {
            GardenXpText = $"{Format.Number(xp)} XP · MAX";
            GardenProgress = 1;
            GardenNextText = "Your garden is fully grown!";
        }
        else
        {
            long start = GardenProgression.TotalXpFor(level);
            long end = GardenProgression.TotalXpFor(level + 1);
            GardenXpText = $"{Format.Number(xp - start)} / {Format.Number(end - start)} XP";
            GardenProgress = GardenProgression.ProgressToNext(xp);

            var unlocks = Crops.All.Where(c => c.UnlockGardenLevel == level + 1).Select(c => c.Name).ToList();
            if (GardenProgression.PlotUnlocks.Any(p => p.GardenLevel == level + 1)) unlocks.Add("a new plot");
            GardenNextText = (unlocks.Count > 0
                    ? $"Level {level + 1} unlocks {string.Join(" and ", unlocks)}. "
                    : $"Level {level + 1}: keep growing! ")
                + "Earn Garden XP from every harvest and from collection tiers.";
        }

        while (Plots.Count < Engine.Plots.Count) Plots.Add(new PlotRow(Plots.Count));
        while (Plots.Count > Engine.Plots.Count) Plots.RemoveAt(Plots.Count - 1);
        foreach (var row in Plots) row.Update(_host);
        PlotsHeader = $"Plots ({Engine.Plots.Count} / {GardenProgression.MaxPlots})";

        CanUnlockPlot = Engine.CanUnlockNextPlot(out var cost, out string reason);
        HasNextPlot = cost is not null;
        NextPlotTitle = cost is null ? "" : $"Unlock plot {Engine.Plots.Count + 1}";
        NextPlotText = cost is null
            ? ""
            : $"{Format.Number(cost.Coins)} coins · Garden Level {cost.GardenLevel}" + (CanUnlockPlot ? "" : $"\n{reason}");
    }

    private void RefreshFarming()
    {
        int level = Engine.FarmingLevel;
        long xp = Engine.State.FarmingXp;
        FarmingLevelText = $"Farming Level {level}";

        if (level >= FarmingSkill.MaxLevel)
        {
            FarmingXpText = $"{Format.Number(xp)} XP · MAX";
            FarmingProgress = 1;
            FarmingNextText = "Max level. You're a master farmer!";
        }
        else
        {
            long start = FarmingSkill.TotalXpFor(level);
            long end = FarmingSkill.TotalXpFor(level + 1);
            FarmingXpText = $"{Format.Number(xp - start)} / {Format.Number(end - start)} XP";
            FarmingProgress = FarmingSkill.ProgressToNext(xp);
            FarmingNextText = $"Level {level + 1} gives +{FarmingSkill.FortunePerLevel} Harvest Fortune and "
                              + $"{Format.Number((long)(level + 1) * FarmingSkill.CoinRewardPerLevel)} coins.";
        }

        FortuneLevelText = $"+{level * FarmingSkill.FortunePerLevel} from Farming Level {level}";
        var stats = Engine.State.Stats;
        StatsText = $"Harvests: {Format.Number(stats.TotalHarvests)} (by hand: {Format.Number(stats.HandHarvests)})\n"
                    + $"Coins earned from selling: {Format.Number(stats.CoinsEarned)}";
        foreach (var row in CropGuide) row.Update(Engine);
    }

    private void RefreshMarket()
    {
        var held = Crops.All.Where(c => Engine.GetInventory(c) > 0).ToList();
        if (!held.SequenceEqual(Inventory.Select(r => r.Crop)))
        {
            Inventory.Clear();
            foreach (var crop in held)
            {
                var id = crop.Id;
                Inventory.Add(new InventoryRow(crop, _host.Sprites.CropIcon(crop), new RelayCommand(() =>
                {
                    Engine.Sell(id, long.MaxValue);
                    Refresh();
                })));
            }
        }
        foreach (var row in Inventory) row.Update(Engine);

        long value = Engine.InventoryValue();
        InventoryValueText = Format.Number(value);
        HasInventory = value > 0;
    }
}
