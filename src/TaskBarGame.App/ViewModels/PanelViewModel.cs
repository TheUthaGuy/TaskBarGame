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

/// <summary>Everything the panel shows. <see cref="Refresh"/> pulls fresh numbers from the game.</summary>
public sealed partial class PanelViewModel : ObservableObject
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
    private string _fieldTitle = "";
    private string _fieldText = "";
    private ImageSource? _fieldCropIcon;
    private ImageSource? _activeToolIcon;
    private string _activeToolText = "";
    private bool _hasExpansion;
    private bool _canExpand;
    private string _expansionTitle = "";
    private string _expansionText = "";
    private string _farmingLevelText = "";
    private string _farmingXpText = "";
    private double _farmingProgress;
    private string _farmingNextText = "";
    private string _fortuneTitle = "";
    private IReadOnlyList<string> _fortuneLines = Array.Empty<string>();
    private string _speedText = "";
    private string _sellBonusText = "";
    private string _xpBonusText = "";
    private string _statsText = "";
    private string _cropsValueText = "";
    private bool _hasCrops;
    private bool _hasInventory;

    public PanelViewModel(GameHost host)
    {
        _host = host;
        CoinIcon = host.Sprites["coin"];
        FarmerIcon = host.Sprites["farmer_idle"];
        LockIcon = host.Sprites["lock"];

        CropChoices = Crops.All
            .Select(c => new CropChoiceRow(c, host.Sprites.CropIcon(c), new RelayCommand(() =>
            {
                Engine.PlantField(c.Id);
                Refresh();
            })))
            .ToList();
        CropGuide = Crops.All.Select(c => new CropGuideRow(c, host.Sprites.CropIcon(c))).ToList();
        CollectionRows = Crops.All.Select(c => new CollectionRow(c, host.Sprites.CropIcon(c))).ToList();
        ToolRows = Crops.All.Select(c => new UpgradeRow(host, UpgradeKind.Tool, c.Id)).ToList();
        ArmourRows = Armour.Slots.Select(s => new UpgradeRow(host, UpgradeKind.Armour, s.ToString())).ToList();
        TalismanRows = Talismans.Families.Select(f => new UpgradeRow(host, UpgradeKind.Talisman, f.ToString())).ToList();

        ExpandCommand = new RelayCommand(() =>
        {
            if (Engine.TryExpandField(out _)) _host.Save();
            Refresh();
        });
        SellAllCropsCommand = new RelayCommand(() =>
        {
            Engine.SellAllCrops();
            Refresh();
        });
        PackEverythingCommand = new RelayCommand(() =>
        {
            foreach (var item in Items.All) Engine.Pack(item.Id);
            Refresh();
        });
        InitBazaar();
        Refresh();
    }

    private GameEngine Engine => _host.Engine;
    private GameSettings Settings => _host.Settings;

    public ImageSource CoinIcon { get; }
    public ImageSource FarmerIcon { get; }
    public ImageSource LockIcon { get; }
    public string CoinsText { get => _coinsText; private set => Set(ref _coinsText, value); }

    // ---------------------------------------------------------------- Field tab

    public string GardenLevelText { get => _gardenLevelText; private set => Set(ref _gardenLevelText, value); }
    public string GardenXpText { get => _gardenXpText; private set => Set(ref _gardenXpText, value); }
    public double GardenProgress { get => _gardenProgress; private set => Set(ref _gardenProgress, value); }
    public string GardenNextText { get => _gardenNextText; private set => Set(ref _gardenNextText, value); }
    public string FieldTitle { get => _fieldTitle; private set => Set(ref _fieldTitle, value); }
    public string FieldText { get => _fieldText; private set => Set(ref _fieldText, value); }
    public ImageSource? FieldCropIcon { get => _fieldCropIcon; private set => Set(ref _fieldCropIcon, value); }
    public ImageSource? ActiveToolIcon { get => _activeToolIcon; private set => Set(ref _activeToolIcon, value); }
    public string ActiveToolText { get => _activeToolText; private set => Set(ref _activeToolText, value); }
    public IReadOnlyList<CropChoiceRow> CropChoices { get; }
    public bool HasExpansion { get => _hasExpansion; private set => Set(ref _hasExpansion, value); }
    public bool CanExpand { get => _canExpand; private set => Set(ref _canExpand, value); }
    public string ExpansionTitle { get => _expansionTitle; private set => Set(ref _expansionTitle, value); }
    public string ExpansionText { get => _expansionText; private set => Set(ref _expansionText, value); }
    public ICommand ExpandCommand { get; }

    // ---------------------------------------------------------------- Farming tab

    public string FarmingLevelText { get => _farmingLevelText; private set => Set(ref _farmingLevelText, value); }
    public string FarmingXpText { get => _farmingXpText; private set => Set(ref _farmingXpText, value); }
    public double FarmingProgress { get => _farmingProgress; private set => Set(ref _farmingProgress, value); }
    public string FarmingNextText { get => _farmingNextText; private set => Set(ref _farmingNextText, value); }
    public string FortuneTitle { get => _fortuneTitle; private set => Set(ref _fortuneTitle, value); }
    public IReadOnlyList<string> FortuneLines { get => _fortuneLines; private set => Set(ref _fortuneLines, value); }
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }
    public string SellBonusText { get => _sellBonusText; private set => Set(ref _sellBonusText, value); }
    public string XpBonusText { get => _xpBonusText; private set => Set(ref _xpBonusText, value); }
    public string StatsText { get => _statsText; private set => Set(ref _statsText, value); }
    public IReadOnlyList<CropGuideRow> CropGuide { get; }

    // ---------------------------------------------------------------- Upgrades tab

    public IReadOnlyList<UpgradeRow> ToolRows { get; }
    public IReadOnlyList<UpgradeRow> ArmourRows { get; }
    public IReadOnlyList<UpgradeRow> TalismanRows { get; }
    public ObservableCollection<UpgradeRow> UnlockedToolRows { get; } = new();

    // ---------------------------------------------------------------- Collections tab

    public IReadOnlyList<CollectionRow> CollectionRows { get; }

    // ---------------------------------------------------------------- Market tab

    public ObservableCollection<InventoryRow> Inventory { get; } = new();
    public string CropsValueText { get => _cropsValueText; private set => Set(ref _cropsValueText, value); }
    public bool HasCrops { get => _hasCrops; private set => Set(ref _hasCrops, value); }
    public bool HasInventory { get => _hasInventory; private set => Set(ref _hasInventory, value); }
    public ICommand SellAllCropsCommand { get; }
    public ICommand PackEverythingCommand { get; }

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
                MessageBox.Show($"Windows didn't allow that change.\n\n{ex.Message}", App.Title,
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
        CoinsText = Format.Number(Engine.State.Coins);
        RefreshField();
        RefreshFarming();
        RefreshUpgrades();
        foreach (var row in CollectionRows) row.Update(Engine);
        RefreshMarket();
        RefreshBazaar();
    }

    private void RefreshField()
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
            if (FieldProgression.Expansions.Any(e => e.GardenLevel == level + 1)) unlocks.Add("a field expansion");
            int recipes = Recipes.All.Count(r => r.Kind != UpgradeKind.Tool && r.GardenLevel == level + 1);
            if (recipes > 0) unlocks.Add($"{recipes} new upgrade{(recipes == 1 ? "" : "s")}");
            GardenNextText = (unlocks.Count > 0
                    ? $"Level {level + 1} unlocks {string.Join(", ", unlocks)}. "
                    : $"Level {level + 1}: keep farming! ")
                + "Every crop you break gives Garden XP, and collection tiers give more.";
        }

        var crop = Engine.FieldCrop;
        FieldCropIcon = _host.Sprites.CropIcon(crop);
        FieldTitle = $"{Engine.FieldLength} {crop.Name} in a row";
        FieldText = $"The farmer laps the row every {Format.Number(Engine.LapSeconds, 1)}s (it regrows in "
                    + $"{Format.Number(crop.RegrowSeconds, 1)}s) · {Format.Number(Engine.GetFortune(crop, false))} Harvest Fortune";
        int toolTier = Engine.ActiveTool.Tier;
        ActiveToolIcon = toolTier > 0 ? _host.Sprites.ToolIcon(toolTier) : _host.Sprites.ToolIcon(0);
        ActiveToolText = toolTier > 0
            ? $"The farmer is using the {Tools.Name(crop, toolTier)} (+{Tools.Fortune(toolTier)} Fortune, +{Tools.CounterFortune(Engine.ActiveTool.Breaks)} from {Format.Number(Engine.ActiveTool.Breaks)} broken)."
            : $"The farmer has no {crop.Name} Hoe yet. Craft one in Upgrades for more crops.";
        foreach (var row in CropChoices) row.Update(Engine);

        CanExpand = Engine.CanExpandField(out var next, out string reason);
        HasExpansion = next is not null;
        ExpansionTitle = next is null ? "" : $"Expand to {next.Length} crops";
        ExpansionText = next is null
            ? ""
            : $"{Format.Number(next.Coins)} coins · Garden Level {next.GardenLevel}" + (CanExpand ? "" : $"\n{reason}");
    }

    private void RefreshFarming()
    {
        int level = Engine.FarmingLevel;
        double xp = Engine.State.FarmingXp;
        FarmingLevelText = $"Farming Level {level}";

        if (level >= FarmingSkill.MaxLevel)
        {
            FarmingXpText = $"{Format.Number((long)xp)} XP · MAX";
            FarmingProgress = 1;
            FarmingNextText = "Max level. You're a master farmer!";
        }
        else
        {
            long start = FarmingSkill.TotalXpFor(level);
            long end = FarmingSkill.TotalXpFor(level + 1);
            FarmingXpText = $"{Format.Number((long)(xp - start))} / {Format.Number(end - start)} XP";
            FarmingProgress = FarmingSkill.ProgressToNext(xp);
            FarmingNextText = $"Level {level + 1} gives +{FarmingSkill.FortunePerLevel} Harvest Fortune and "
                              + $"{Format.Number((long)(level + 1) * FarmingSkill.CoinRewardPerLevel)} coins.";
        }

        var crop = Engine.FieldCrop;
        FortuneTitle = $"Harvest Fortune for {crop.Name}: {Engine.GetFortune(crop, false)}";
        FortuneLines = Engine.FortuneSources(crop).Select(s => $"+{s.Amount}  {s.Source}").ToList();
        SpeedText = $"Farming Speed: +{Engine.SpeedPercent}% (walks {Format.Number(Engine.FarmerSpeed, 1)} crops a second)";
        SellBonusText = $"Market sell price: +{Engine.SellPercent}%";
        XpBonusText = $"Farming XP: +{Engine.XpPercent}%";

        var stats = Engine.State.Stats;
        StatsText = $"Crops broken: {Format.Number(stats.TotalBreaks)} (by hand: {Format.Number(stats.HandBreaks)})\n"
                    + $"Coins earned from selling: {Format.Number(stats.CoinsEarned)}\n"
                    + $"Upgrades crafted: {Format.Number(stats.ItemsCrafted)}";
        foreach (var row in CropGuide) row.Update(Engine);
    }

    private void RefreshUpgrades()
    {
        var unlocked = ToolRows.Where(r => Engine.IsCropUnlocked(Crops.Get(r.Target))).ToList();
        if (!unlocked.SequenceEqual(UnlockedToolRows))
        {
            UnlockedToolRows.Clear();
            foreach (var row in unlocked) UnlockedToolRows.Add(row);
        }
        foreach (var row in UnlockedToolRows.Concat(ArmourRows).Concat(TalismanRows)) row.Update();
    }

    private void RefreshMarket()
    {
        var held = Items.All.Where(i => Engine.GetItem(i) > 0).ToList();
        if (!held.SequenceEqual(Inventory.Select(r => r.Item)))
        {
            Inventory.Clear();
            foreach (var item in held)
            {
                var id = item.Id;
                Inventory.Add(new InventoryRow(item, _host.Sprites.CropIcon(item.Crop), _host.Sprites.ItemContainer(item),
                    new RelayCommand(() =>
                    {
                        Engine.Sell(id, long.MaxValue);
                        Refresh();
                    }),
                    new RelayCommand(() =>
                    {
                        Engine.Pack(id);
                        Refresh();
                    })));
            }
        }
        foreach (var row in Inventory) row.Update(Engine);

        long value = Engine.CropsValue();
        CropsValueText = Format.Number(value);
        HasCrops = value > 0;
        HasInventory = held.Count > 0;
    }
}
