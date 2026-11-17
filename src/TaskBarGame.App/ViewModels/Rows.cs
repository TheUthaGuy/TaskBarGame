using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>One crop in the Field tab's "plant the field with" picker.</summary>
public sealed class CropChoiceRow : ObservableObject
{
    private bool _isUnlocked;
    private bool _isPlanted;
    private string _subtitle = "";

    public CropChoiceRow(CropDef crop, ImageSource icon, ICommand plant)
    {
        Crop = crop;
        Icon = icon;
        PlantCommand = plant;
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public ICommand PlantCommand { get; }
    public bool IsUnlocked { get => _isUnlocked; private set => Set(ref _isUnlocked, value); }
    public bool IsPlanted { get => _isPlanted; private set => Set(ref _isPlanted, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }
    public double RowOpacity => IsUnlocked ? 1 : 0.45;

    public void Update(GameEngine engine)
    {
        IsUnlocked = engine.IsCropUnlocked(Crop);
        IsPlanted = engine.FieldCrop == Crop;
        Subtitle = IsUnlocked ? Tools.Name(Crop, engine.GetTool(Crop).Tier) : $"Garden Level {Crop.UnlockGardenLevel}";
        Raise(nameof(RowOpacity));
    }
}

/// <summary>One hoe, armour slot or talisman in the Upgrades tab: what you have and what's next.</summary>
public sealed class UpgradeRow : ObservableObject
{
    private readonly GameHost _host;
    private ImageSource? _icon;
    private string _title = "";
    private string _effect = "";
    private string _nextTitle = "";
    private string _nextEffect = "";
    private string _cost = "";
    private string _status = "";
    private bool _canCraft;
    private bool _hasNext;
    private bool _hasMissing;
    private bool _canBuyMissing;
    private string _buyMissingText = "";
    private string _buyMissingTip = "";
    private Recipe? _next;

    public UpgradeRow(GameHost host, UpgradeKind kind, string target)
    {
        _host = host;
        Kind = kind;
        Target = target;
        CraftCommand = new RelayCommand(() =>
        {
            if (_next is not null && _host.Engine.TryCraft(_next, out _)) _host.Save();
        });
        BuyMissingCommand = new RelayCommand(() =>
        {
            if (_next is not null && _host.Engine.BuyMissingIngredients(_next, out _) > 0) _host.Save();
        });
    }

    public UpgradeKind Kind { get; }
    public string Target { get; }
    public ICommand CraftCommand { get; }
    public ICommand BuyMissingCommand { get; }

    /// <summary>The next upgrade is only missing ingredients (which the Bazaar sells).</summary>
    public bool HasMissing { get => _hasMissing; private set => Set(ref _hasMissing, value); }
    public bool CanBuyMissing { get => _canBuyMissing; private set => Set(ref _canBuyMissing, value); }
    public string BuyMissingText { get => _buyMissingText; private set => Set(ref _buyMissingText, value); }
    public string BuyMissingTip { get => _buyMissingTip; private set => Set(ref _buyMissingTip, value); }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Effect { get => _effect; private set => Set(ref _effect, value); }
    public string NextTitle { get => _nextTitle; private set => Set(ref _nextTitle, value); }
    public string NextEffect { get => _nextEffect; private set => Set(ref _nextEffect, value); }
    public string Cost { get => _cost; private set => Set(ref _cost, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanCraft { get => _canCraft; private set => Set(ref _canCraft, value); }
    public bool HasNext { get => _hasNext; private set => Set(ref _hasNext, value); }

    public void Update()
    {
        var engine = _host.Engine;
        int tier = engine.GetTier(Kind, Target);
        Icon = IconFor(tier);
        Title = NameFor(tier);
        Effect = Describe.Effect(Kind, Target, tier);

        _next = engine.NextRecipe(Kind, Target);
        HasNext = _next is not null;
        if (_next is null)
        {
            NextTitle = "Fully upgraded!";
            NextEffect = Cost = Status = "";
            CanCraft = HasMissing = false;
            return;
        }
        NextTitle = "Next: " + _next.ResultName;
        NextEffect = Describe.Effect(engine, _next);
        Cost = Describe.Cost(engine, _next);
        CanCraft = engine.CanCraft(_next, out string reason);
        Status = CanCraft ? "Ready to craft!" : reason;

        var missing = engine.MissingIngredients(_next);
        HasMissing = missing.Count > 0 && engine.CraftBlocker(_next) is null;
        if (HasMissing)
        {
            long cost = engine.MissingIngredientsCost(_next);
            long coins = engine.State.Coins;
            CanBuyMissing = coins >= cost;
            BuyMissingText = $"Buy missing · {Format.Number(cost)}";
            BuyMissingTip = $"Buy {Describe.Items(missing)} on the Bazaar for {Format.Coins(cost)}."
                            + (CanBuyMissing ? "" : $" You need {Format.Number(cost - coins)} more coins.");
        }
    }

    private ImageSource IconFor(int tier) => Kind switch
    {
        UpgradeKind.Tool => tier > 0 ? _host.Sprites.ToolIcon(tier) : _host.Sprites.CropIcon(Crops.Get(Target)),
        UpgradeKind.Armour => _host.Sprites.ArmourIcon(Enum.Parse<ArmourSlot>(Target), tier),
        UpgradeKind.Compactor => _host.Sprites.CompactorIcon(tier),
        _ => _host.Sprites.TalismanIcon(Enum.Parse<TalismanFamily>(Target), tier),
    };

    private string NameFor(int tier) => Kind switch
    {
        UpgradeKind.Tool => Tools.Name(Crops.Get(Target), tier) + (tier == 0 ? $" for {Crops.Get(Target).Name}" : ""),
        UpgradeKind.Armour => Armour.Name(Enum.Parse<ArmourSlot>(Target), tier),
        UpgradeKind.Compactor => Compactors.Name(tier),
        _ => Talismans.Name(Enum.Parse<TalismanFamily>(Target), tier),
    };
}

/// <summary>One crop in the Farming tab's crop guide.</summary>
public sealed class CropGuideRow : ObservableObject
{
    private static readonly Brush Gold = Format.Brush("#F5C542");
    private static readonly Brush Muted = Format.Brush("#A3B19F");

    private string _details = "";
    private string _badge = "";
    private Brush _badgeBrush = Muted;
    private double _rowOpacity = 1;
    private bool _isBest;

    public CropGuideRow(CropDef crop, ImageSource icon)
    {
        Crop = crop;
        Icon = icon;
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public string Details { get => _details; private set => Set(ref _details, value); }
    public string Badge { get => _badge; private set => Set(ref _badge, value); }
    public Brush BadgeBrush { get => _badgeBrush; private set => Set(ref _badgeBrush, value); }
    public double RowOpacity { get => _rowOpacity; private set => Set(ref _rowOpacity, value); }

    /// <summary>The unlocked crop that makes the most coins an hour.</summary>
    public bool IsBest { get => _isBest; private set => Set(ref _isBest, value); }

    public void Update(GameEngine engine, CropDef best)
    {
        double drops = engine.ExpectedDrops(Crop, handBreak: false);
        Details = $"{Format.Number(drops, 1)} per crop broken (worth {Format.Number(drops * engine.MarketPrice(Items.Raw(Crop)), 1)} coins) · "
                  + $"{Crop.FarmingXp} Farming XP";

        bool unlocked = engine.IsCropUnlocked(Crop);
        Badge = unlocked
            ? $"≈ {Format.Number((long)Math.Round(engine.CoinsPerHour(Crop)))} coins an hour"
            : $"Garden Level {Crop.UnlockGardenLevel}";
        BadgeBrush = unlocked ? Gold : Muted;
        RowOpacity = unlocked ? 1 : 0.5;
        IsBest = unlocked && Crop == best;
    }
}

/// <summary>One crop in the Collections tab.</summary>
public sealed class CollectionRow : ObservableObject
{
    private string _tierText = "";
    private string _countText = "";
    private string _rewardText = "";
    private string _maxText = "";
    private string _tiersTip = "";
    private double _progress;
    private double _rowOpacity = 1;

    public CollectionRow(CropDef crop, ImageSource icon)
    {
        Crop = crop;
        Icon = icon;
        ProgressBrush = CropBrushes.Get(crop);
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public Brush ProgressBrush { get; }
    public string TierText { get => _tierText; private set => Set(ref _tierText, value); }
    public string CountText { get => _countText; private set => Set(ref _countText, value); }
    public string RewardText { get => _rewardText; private set => Set(ref _rewardText, value); }

    /// <summary>"Max (Tier X): 2,000,000 · 3% of the way there".</summary>
    public string MaxText { get => _maxText; private set => Set(ref _maxText, value); }

    /// <summary>Every tier: how many it needs and what it gives (the row's tooltip).</summary>
    public string TiersTip { get => _tiersTip; private set => Set(ref _tiersTip, value); }

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public double RowOpacity { get => _rowOpacity; private set => Set(ref _rowOpacity, value); }

    public void Update(GameEngine engine)
    {
        long collected = engine.GetCollection(Crop);
        int tier = CollectionTiers.TierFor(Crop, collected);
        var thresholds = CollectionTiers.Thresholds(Crop);
        long? next = CollectionTiers.NextThreshold(Crop, collected);
        long previous = tier == 0 ? 0 : thresholds[tier - 1];

        long max = CollectionTiers.MaxThreshold(Crop);
        string maxTier = Format.Roman(CollectionTiers.MaxTier);
        TierText = tier == 0 ? $"Not started · {CollectionTiers.MaxTier} tiers"
            : next is null ? $"Tier {Format.Roman(tier)} · MAXED"
            : $"Tier {Format.Roman(tier)} of {maxTier}";
        MaxText = next is null
            ? $"Maxed out: Tier {maxTier} needed {Format.Number(max)} {Crop.Name}"
            : $"Max (Tier {maxTier}): {Format.Number(max)} {Crop.Name} · {MaxProgress(collected, max)} of the way there";
        TiersTip = string.Join("\n", thresholds.Select((t, i) =>
            $"{(i < tier ? "✓" : "   ")} Tier {Format.Roman(i + 1)}: {Format.Number(t)}  (+{(i + 1) * CollectionTiers.FortunePerTier} {Crop.Name} Fortune)"));
        CountText = next is null
            ? $"{Format.Number(collected)} collected"
            : $"{Format.Number(collected)} / {Format.Number(next.Value)}";
        Progress = next is null ? 1 : (double)(collected - previous) / (next.Value - previous);
        RewardText = tier == 0
            ? $"Tier I: +{CollectionTiers.FortunePerTier} {Crop.Name} Fortune"
            : $"+{tier * CollectionTiers.FortunePerTier} {Crop.Name} Fortune";
        RowOpacity = engine.IsCropUnlocked(Crop) || collected > 0 ? 1 : 0.45;
    }

    /// <summary>"3%", or "under 1%" so a started collection never reads 0%.</summary>
    private static string MaxProgress(long collected, long max)
    {
        double percent = 100.0 * collected / max;
        return collected > 0 && percent < 1 ? "under 1%" : $"{Math.Floor(percent):0}%";
    }
}

/// <summary>One stack of items in the Inventory tab: crops, packed crops or dense crops.</summary>
public sealed class InventoryRow : ObservableObject
{
    private readonly GameHost _host;
    private string _countText = "";
    private string _keptText = "";
    private string _sellText = "";
    private bool _canSell;
    private bool _canPack;
    private string _packTip = "";
    private bool _isLocked;

    public InventoryRow(GameHost host, ItemDef item, ImageSource icon, ImageSource? container, ICommand sell)
    {
        _host = host;
        Item = item;
        Icon = icon;
        Container = container;
        SellCommand = sell;
    }

    public ItemDef Item { get; }
    public ImageSource Icon { get; }

    /// <summary>The sack or crate drawn behind packed and dense crops.</summary>
    public ImageSource? Container { get; }

    public bool HasContainer => Container is not null;
    public string Name => Item.Name;
    public ICommand SellCommand { get; }
    public string CountText { get => _countText; private set => Set(ref _countText, value); }

    /// <summary>"Keeping 240 for upgrades", or "" when nothing is kept.</summary>
    public string KeptText { get => _keptText; private set => Set(ref _keptText, value); }

    public bool HasKeptText => KeptText.Length > 0;
    public string SellText { get => _sellText; private set => Set(ref _sellText, value); }
    public bool CanSell { get => _canSell; private set => Set(ref _canSell, value); }
    public bool CanPack { get => _canPack; private set => Set(ref _canPack, value); }
    public string PackTip { get => _packTip; private set => Set(ref _packTip, value); }

    /// <summary>Locked items are never sold, by any button.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (!Set(ref _isLocked, value)) return;
            _host.Engine.SetLocked(Item, value);
            _host.Save();
        }
    }

    /// <summary>How many you could pack right now.</summary>
    public long MaxPacks { get; private set; }

    /// <summary>The choices in the Pack menu: (label, how many to make).</summary>
    public IReadOnlyList<(string Label, long Count)> PackChoices
    {
        get
        {
            var choices = new List<(string, long)>();
            if (Item.PacksInto is not { } into || MaxPacks <= 0) return choices;
            foreach (long n in new long[] { 1, 5, 10, 25, 50, 100 })
                if (n < MaxPacks) choices.Add(($"{Format.Number(n)} {into.Name}  (uses {Format.Number(n * ItemDef.PackSize)})", n));
            choices.Add(($"All: {Format.Number(MaxPacks)} {into.Name}  (uses {Format.Number(MaxPacks * ItemDef.PackSize)})", MaxPacks));
            return choices;
        }
    }

    public void Update(GameEngine engine)
    {
        long count = engine.GetItem(Item);
        double price = engine.MarketPrice(Item);
        CountText = $"x{Format.Number(count)} · {Format.Price(Math.Round(price, 1))} coins each at the Market";

        Set(ref _isLocked, engine.IsLocked(Item), nameof(IsLocked));
        long kept = engine.KeptForUpgrades(Item);
        KeptText = IsLocked ? "Locked: never sold"
            : kept > 0 ? (kept == count ? "All kept for your next upgrades" : $"Keeping {Format.Number(kept)} for your next upgrades")
            : "";
        Raise(nameof(HasKeptText));

        long sellable = engine.SellableAmount(Item);
        CanSell = sellable > 0;
        SellText = IsLocked ? "Locked"
            : sellable <= 0 ? "Kept for upgrades"
            : sellable == count ? $"Sell for {Format.Number((long)Math.Floor(count * price))}"
            : $"Sell {Format.Number(sellable)} for {Format.Number((long)Math.Floor(sellable * price))}";

        MaxPacks = Item.PacksInto is null ? 0 : count / ItemDef.PackSize;
        CanPack = MaxPacks > 0;
        PackTip = Item.PacksInto is null ? "" : $"Pack {ItemDef.PackSize} {Item.Name} into 1 {Item.PacksInto.Name}. Choose how many.";
    }
}

/// <summary>One crop in the Inventory tab's "Auto-sell &amp; Compactor" list.</summary>
public sealed class CropSettingRow : ObservableObject
{
    private readonly GameHost _host;
    private bool _autoSell;
    private bool _compact;
    private bool _isUnlocked;
    private bool _hasCompactor;
    private string _keptText = "";

    public CropSettingRow(GameHost host, CropDef crop, ImageSource icon)
    {
        _host = host;
        Crop = crop;
        Icon = icon;
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public bool IsUnlocked { get => _isUnlocked; private set => Set(ref _isUnlocked, value); }
    public bool HasCompactor { get => _hasCompactor; private set => Set(ref _hasCompactor, value); }
    public string KeptText { get => _keptText; private set => Set(ref _keptText, value); }

    public bool AutoSell
    {
        get => _autoSell;
        set
        {
            if (!Set(ref _autoSell, value)) return;
            _host.Engine.SetAutoSell(Crop, value);
            _host.Save();
        }
    }

    public bool Compact
    {
        get => _compact;
        set
        {
            if (!Set(ref _compact, value)) return;
            _host.Engine.SetCompacting(Crop, value);
            _host.Save();
        }
    }

    public void Update(GameEngine engine)
    {
        IsUnlocked = engine.IsCropUnlocked(Crop);
        HasCompactor = engine.State.Compactor > 0;
        Set(ref _autoSell, engine.IsAutoSelling(Crop), nameof(AutoSell));
        Set(ref _compact, engine.IsCompacting(Crop), nameof(Compact));

        if (!engine.State.Settings.KeepForUpgrades)
        {
            KeptText = "";
            return;
        }
        var needs = engine.UpgradeNeeds(Crop);
        var parts = new List<string>();
        if (needs.Raw > 0) parts.Add($"{Format.Number(needs.Raw)} {Crop.Name}");
        if (needs.Packed > 0) parts.Add($"{Format.Number(needs.Packed)} Packed");
        if (needs.Dense > 0) parts.Add($"{Format.Number(needs.Dense)} Dense");
        KeptText = parts.Count == 0 ? "Nothing needed right now" : "Upgrades need " + string.Join(" + ", parts);
    }
}

/// <summary>Shared, cached brushes in each crop's colour.</summary>
internal static class CropBrushes
{
    private static readonly System.Collections.Generic.Dictionary<string, Brush> ByCrop = new();

    public static Brush Get(CropDef crop)
    {
        if (!ByCrop.TryGetValue(crop.Id, out var brush))
            ByCrop[crop.Id] = brush = Format.Brush(crop.ColorHex);
        return brush;
    }
}

/// <summary>One item in the Bazaar list: its live prices and how they've moved.</summary>
public sealed class BazaarRow : ObservableObject
{
    private static readonly Brush Up = Format.Brush("#7BC950");
    private static readonly Brush Down = Format.Brush("#E5675A");
    private static readonly Brush Flat = Format.Brush("#A3B19F");

    private string _priceText = "";
    private string _trendText = "";
    private Brush _trendBrush = Flat;
    private bool _isSelected;

    public BazaarRow(ItemDef item, ImageSource icon, ImageSource? container, ICommand select)
    {
        Item = item;
        Icon = icon;
        Container = container;
        SelectCommand = select;
    }

    public ItemDef Item { get; }
    public ImageSource Icon { get; }
    public ImageSource? Container { get; }
    public bool HasContainer => Container is not null;
    public string Name => Item.Name;
    public ICommand SelectCommand { get; }
    public string PriceText { get => _priceText; private set => Set(ref _priceText, value); }
    public string TrendText { get => _trendText; private set => Set(ref _trendText, value); }
    public Brush TrendBrush { get => _trendBrush; private set => Set(ref _trendBrush, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>For the search box: "mel" finds Melon, Packed Melon and Dense Melon.</summary>
    public bool Matches(string search) =>
        string.IsNullOrWhiteSpace(search) || Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Update(GameEngine engine)
    {
        PriceText = $"Sell {Format.Price(engine.BazaarSellPrice(Item))} · Buy {Format.Price(engine.BazaarBuyPrice(Item))}";
        double trend = engine.BazaarTrend(Item);
        TrendText = Format.Trend(trend);
        TrendBrush = trend > 0.005 ? Up : trend < -0.005 ? Down : Flat;
    }
}

/// <summary>One of your buy orders or sell offers on the Bazaar.</summary>
public sealed class OrderRow : ObservableObject
{
    private string _progressText = "";
    private double _progress;

    public OrderRow(BazaarOrder order, ImageSource icon, ImageSource? container, ICommand cancel)
    {
        Order = order;
        Icon = icon;
        Container = container;
        CancelCommand = cancel;
        var item = Items.Get(order.ItemId);
        Title = order.Kind == BazaarOrderKind.BuyOrder
            ? $"Buy order: {Format.Number(order.Amount)} {item.Name} at {Format.Price(order.Price)} each"
            : $"Sell offer: {Format.Number(order.Amount)} {item.Name} at {Format.Price(order.Price)} each";
    }

    public BazaarOrder Order { get; }
    public ImageSource Icon { get; }
    public ImageSource? Container { get; }
    public bool HasContainer => Container is not null;
    public string Title { get; }
    public ICommand CancelCommand { get; }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public void Update()
    {
        Progress = (double)Order.Filled / Order.Amount;
        ProgressText = Order.Filled == 0
            ? "Waiting for the price to come to you"
            : $"{Format.Number(Order.Filled)} of {Format.Number(Order.Amount)} filled";
    }
}
