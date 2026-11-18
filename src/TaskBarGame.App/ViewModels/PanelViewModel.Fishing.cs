using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>The Fishing tab: where the farmer is, Fishing level and stats, rods, armour, bait, trades and the catch log.</summary>
public sealed partial class PanelViewModel
{
    private string _fishingLevelText = "";
    private string _fishingXpText = "";
    private double _fishingProgress;
    private string _fishingNextText = "";
    private string _fishingWhereTitle = "";
    private string _fishingWhereText = "";
    private string _fishingStatsText = "";
    private string _fishingSourcesText = "";
    private string _fishingRatesHeadline = "";
    private string _fishingRatesDetail = "";
    private string _fishingMessage = "";
    private string _bitingTitle = "";
    private string _catchLogTitle = "";
    private string _junkShopText = "";
    private bool _junkShopOpen;
    private int _readyFishingUpgrades;

    public IReadOnlyList<PlaceRow> PlaceRows { get; private set; } = Array.Empty<PlaceRow>();
    public IReadOnlyList<UpgradeRow> RodRows { get; private set; } = Array.Empty<UpgradeRow>();
    public IReadOnlyList<UpgradeRow> FishingArmourRows { get; private set; } = Array.Empty<UpgradeRow>();
    public IReadOnlyList<BaitRow> BaitRows { get; private set; } = Array.Empty<BaitRow>();
    public IReadOnlyList<TradeRow> BaitTableRows { get; private set; } = Array.Empty<TradeRow>();
    public IReadOnlyList<TradeRow> JunkShopRows { get; private set; } = Array.Empty<TradeRow>();
    public IReadOnlyList<BiteRow> BitingRows { get; private set; } = Array.Empty<BiteRow>();
    public IReadOnlyList<CatchLogRow> CatchLogRows { get; private set; } = Array.Empty<CatchLogRow>();

    public string FishingLevelText { get => _fishingLevelText; private set => Set(ref _fishingLevelText, value); }
    public string FishingXpText { get => _fishingXpText; private set => Set(ref _fishingXpText, value); }
    public double FishingProgress { get => _fishingProgress; private set => Set(ref _fishingProgress, value); }
    public string FishingNextText { get => _fishingNextText; private set => Set(ref _fishingNextText, value); }
    public string FishingWhereTitle { get => _fishingWhereTitle; private set => Set(ref _fishingWhereTitle, value); }
    public string FishingWhereText { get => _fishingWhereText; private set => Set(ref _fishingWhereText, value); }
    public string FishingStatsText { get => _fishingStatsText; private set => Set(ref _fishingStatsText, value); }
    public string FishingSourcesText { get => _fishingSourcesText; private set => Set(ref _fishingSourcesText, value); }
    public string FishingRatesHeadline { get => _fishingRatesHeadline; private set => Set(ref _fishingRatesHeadline, value); }
    public string FishingRatesDetail { get => _fishingRatesDetail; private set => Set(ref _fishingRatesDetail, value); }

    /// <summary>What just happened in the Fishing tab ("Made 50 Wriggler Bait.").</summary>
    public string FishingMessage { get => _fishingMessage; private set => Set(ref _fishingMessage, value); }

    public string BitingTitle { get => _bitingTitle; private set => Set(ref _bitingTitle, value); }
    public string CatchLogTitle { get => _catchLogTitle; private set => Set(ref _catchLogTitle, value); }
    public string JunkShopText { get => _junkShopText; private set => Set(ref _junkShopText, value); }
    public bool JunkShopOpen { get => _junkShopOpen; private set => Set(ref _junkShopOpen, value); }
    public ImageSource? RowanIcon { get; private set; }

    /// <summary>Rods and fishing armour you can craft right now (shown on the Fishing tab).</summary>
    public int ReadyFishingUpgrades { get => _readyFishingUpgrades; private set => Set(ref _readyFishingUpgrades, value); }

    private void InitFishing()
    {
        void Done(string message)
        {
            FishingMessage = message;
            _host.Save();
            Refresh();
        }

        RowanIcon = _host.Sprites.Rowan;
        PlaceRows = new[] { new PlaceRow(_host, null, Done) }
            .Concat(Places.Lakes.Select(lake => new PlaceRow(_host, lake, Done)))
            .ToList();
        RodRows = new[] { new UpgradeRow(_host, UpgradeKind.Rod, Rods.Target) };
        FishingArmourRows = Armour.Slots.Select(s => new UpgradeRow(_host, UpgradeKind.FishingArmour, s.ToString())).ToList();
        BaitRows = new[] { new BaitRow(_host, null, Done) }
            .Concat(Baits.All.Select(b => new BaitRow(_host, b, Done)))
            .ToList();
        BaitTableRows = Trades.From(TradeSource.BaitTable).Select(t => new TradeRow(_host, t, Done)).ToList();
        JunkShopRows = Trades.From(TradeSource.JunkShop).Select(t => new TradeRow(_host, t, Done)).ToList();
        CatchLogRows = Places.Lakes.SelectMany(l => l.Catches.Select(c => c.Item))
            .Concat(Places.Treasure.Select(t => t.Item))
            .Distinct()
            .Select(item => new CatchLogRow(_host, item))
            .ToList();
    }

    private void RefreshFishing()
    {
        // ---- level
        int level = Engine.FishingLevel;
        double xp = Engine.State.Fishing.Xp;
        FishingLevelText = $"Fishing Level {level}";
        if (level >= FishingSkill.MaxLevel)
        {
            FishingXpText = $"{Format.Number((long)xp)} XP · MAX";
            FishingProgress = 1;
            FishingNextText = "You're a master angler!";
        }
        else
        {
            long start = FishingSkill.TotalXpFor(level), end = FishingSkill.TotalXpFor(level + 1);
            FishingXpText = $"{Format.Number((long)(xp - start))} / {Format.Number(end - start)} XP";
            FishingProgress = FishingSkill.ProgressToNext(xp);
            var unlocks = Places.Lakes.SelectMany(l => l.Catches).Where(c => c.FishingLevel == level + 1)
                .Select(c => c.Item.Name).Distinct().ToList();
            if (Rods.FishingLevel(Engine.RodTier + 1) == level + 1 && Engine.RodTier < Rods.MaxTier)
                unlocks.Add($"the {Rods.Name(Engine.RodTier + 1)}");
            FishingNextText = $"Level {level + 1}: +{FishingSkill.SpeedPerLevel} Fishing Speed, "
                              + $"{Format.Number((long)(level + 1) * FishingSkill.CoinRewardPerLevel)} coins"
                              + (unlocks.Count > 0 ? $", and {string.Join(", ", unlocks)}." : ".");
        }

        // ---- where the farmer is
        var lake = Engine.CurrentLake;
        FishingWhereTitle = lake is null ? "The farmer is at the field" : $"Fishing at {lake.Name}";
        FishingWhereText = lake is null
            ? "Send them to a lake to fish. Your crops keep growing while they're away, so the field is full when they come back."
            : $"{lake.Description} The farmer fishes by themselves, even while the game is closed (at half speed). Your field keeps growing.";
        foreach (var row in PlaceRows) row.Update();

        // ---- stats
        var stats = Engine.FishingStats;
        FishingStatsText = $"Fishing Speed {stats.Speed} · Treasure Chance {Engine.TreasureChance:0.##}% · Double Catch {Engine.DoubleCatchChance:0.##}%\n"
                           + $"A bite every {Format.Number(Engine.AverageWaitSeconds, 1)}s on average · "
                           + $"≈ {Format.Number((long)Engine.CatchesPerHour)} catches an hour";
        FishingSourcesText = string.Join("\n", Engine.FishingSources().Select(s => $"{s.Source}: {s.Bonus.Describe()}"));

        var recent = _host.FishingRates.Recent(_host.Clock);
        if (!Engine.IsFishing)
        {
            FishingRatesHeadline = $"≈ {Format.Number((long)Engine.FishingCoinsPerHour(Engine.LastLake))} coins an hour at {Engine.LastLake.Name}";
            FishingRatesDetail = "Worked out from your stats, selling every catch to the Market.";
        }
        else if (!recent.IsMeasured)
        {
            FishingRatesHeadline = $"≈ {Format.Number((long)Engine.FishingCoinsPerHour(lake!))} coins an hour";
            FishingRatesDetail = "Worked out from your stats. Give it a few minutes of fishing to see the real numbers.";
        }
        else
        {
            FishingRatesHeadline = $"≈ {Format.Number((long)recent.CoinsPerHour)} coins an hour";
            FishingRatesDetail = $"{Format.Number((long)recent.CropsPerHour)} items and {Format.Number((long)recent.XpPerHour)} Fishing XP an hour "
                                 + $"over the last {Format.Duration(recent.Seconds)} (what your catches are worth at the Market).";
        }

        // ---- equipment, bait and trades
        foreach (var row in RodRows.Concat(FishingArmourRows)) row.Update();
        ReadyFishingUpgrades = Engine.ReadyRecipes().Count(r => r.IsFishing);
        foreach (var row in BaitRows) row.Update();
        foreach (var row in BaitTableRows.Concat(JunkShopRows)) row.Update();
        JunkShopOpen = Engine.IsJunkShopOpen;
        JunkShopText = JunkShopOpen
            ? $"\"Junk? I'll take it!\" {Trades.ShopkeeperName} lives at Driftwood Cove and swaps the junk you fish up there for bait, treasure and clothes."
            : $"{Trades.ShopkeeperName} runs a junk shop at Driftwood Cove. Buy the boat trip there to trade with him.";

        // ---- what's biting and the catch log
        var shown = lake ?? Engine.LastLake;
        BitingTitle = $"What's biting at {shown.Name}";
        var biting = Engine.BiteChances(shown);
        if (BitingRows.Count != biting.Count + Places.Treasure.Count || BitingRows.FirstOrDefault()?.Lake != shown)
        {
            BitingRows = biting.Select(b => new BiteRow(_host, shown, b.Entry.Item))
                .Concat(Places.Treasure.Select(t => new BiteRow(_host, shown, t.Item, treasure: t)))
                .ToList();
            Raise(nameof(BitingRows));
        }
        foreach (var row in BitingRows) row.Update();

        int caught = CatchLogRows.Count(r => Engine.TimesCaught(r.Item) > 0);
        CatchLogTitle = $"Catch log: {caught} of {CatchLogRows.Count} found";
        foreach (var row in CatchLogRows) row.Update();
    }
}

/// <summary>The field or a lake, with a button to go there (or buy the boat trip).</summary>
public sealed class PlaceRow : ObservableObject
{
    private readonly GameHost _host;
    private readonly LakeDef? _lake;
    private string _buttonText = "";
    private string _status = "";
    private bool _canUse;
    private bool _isHere;

    public PlaceRow(GameHost host, LakeDef? lake, Action<string> done)
    {
        _host = host;
        _lake = lake;
        Name = lake?.Name ?? "The field";
        Description = lake?.Description ?? "Where your crops grow. The farmer farms the row by themselves.";
        Command = new RelayCommand(() =>
        {
            var engine = _host.Engine;
            if (_lake is { } l && !engine.CanReach(l))
            {
                done(engine.BuyBoatTrip(l, out string why) ? $"Bought the boat trip to {l.Island}! Go there any time." : why);
                return;
            }
            done(engine.TravelTo(_lake?.Id ?? Places.Field, out string reason)
                ? (_lake is null ? "The farmer is back at the field." : $"The farmer is fishing at {_lake.Name}.")
                : reason);
        });
    }

    public string Name { get; }
    public string Description { get; }
    public ICommand Command { get; }
    public ImageSource? Icon { get; private set; }
    public string ButtonText { get => _buttonText; private set => Set(ref _buttonText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanUse { get => _canUse; private set => Set(ref _canUse, value); }
    public bool IsHere { get => _isHere; private set => Set(ref _isHere, value); }

    public void Update()
    {
        var engine = _host.Engine;
        var icon = _lake is null ? _host.Sprites.CropIcon(engine.FieldCrop)
            : _lake.IsOnAnotherIsland ? _host.Sprites.Rowan
            : _host.Sprites.FishingIcon(FishingItems.Perch);
        if (!ReferenceEquals(Icon, icon))
        {
            Icon = icon;
            Raise(nameof(Icon));
        }

        IsHere = engine.Location == (_lake?.Id ?? Places.Field);
        if (IsHere)
        {
            ButtonText = "You're here";
            CanUse = false;
            Status = "";
        }
        else if (_lake is { } lake && !engine.CanReach(lake))
        {
            ButtonText = $"Boat trip · {Format.Number(lake.BoatTripCost)}";
            CanUse = engine.CanBuyBoatTrip(lake, out string reason);
            Status = CanUse ? "Pay once, then travel there for free." : reason;
        }
        else
        {
            ButtonText = _lake is null ? "Go farming" : "Go fishing";
            CanUse = true;
            Status = "";
        }
    }
}

/// <summary>One bait (or "No bait"): what it does, how many you have, and a button to choose it.</summary>
public sealed class BaitRow : ObservableObject
{
    private readonly GameHost _host;
    private string _heldText = "";
    private bool _isChosen;

    public BaitRow(GameHost host, ItemDef? bait, Action<string> done)
    {
        _host = host;
        Bait = bait;
        Icon = bait is null ? host.Sprites.RodIcon(1) : host.Sprites.ItemIcon(bait);
        Name = bait?.Name ?? "No bait";
        Effect = bait is null ? "Fish without bait." : Baits.Bonus[bait.Id].Describe() + " · one used per catch";
        ChooseCommand = new RelayCommand(() =>
        {
            host.Engine.SetBait(bait?.Id);
            done(bait is null ? "Fishing without bait." : $"Using {bait.Name}.");
        });
    }

    public ItemDef? Bait { get; }
    public ImageSource Icon { get; }
    public string Name { get; }
    public string Effect { get; }
    public ICommand ChooseCommand { get; }
    public string HeldText { get => _heldText; private set => Set(ref _heldText, value); }
    public bool IsChosen { get => _isChosen; private set => Set(ref _isChosen, value); }

    public void Update()
    {
        var engine = _host.Engine;
        IsChosen = engine.ChosenBait?.Id == Bait?.Id;
        HeldText = Bait is null ? "" : $"You have {Format.Number(engine.GetItem(Bait))}" + (IsChosen && engine.GetItem(Bait) == 0 ? " (none left!)" : "");
    }
}

/// <summary>One trade: at the bait table, or with Rusty Rowan.</summary>
public sealed class TradeRow : ObservableObject
{
    private readonly GameHost _host;
    private string _status = "";
    private string _makeAllText = "";
    private bool _canMake;
    private bool _canMakeMore;

    public TradeRow(GameHost host, TradeDef trade, Action<string> done)
    {
        _host = host;
        Trade = trade;
        var cosmetic = Cosmetics.Find(trade.Cosmetic);
        Name = cosmetic?.Name ?? $"{Format.Number(trade.ResultCount)} {trade.Result!.Name}";
        Description = cosmetic is not null
            ? (cosmetic.Slot == CosmeticSlot.Hat ? "A hat for the farmer (Wardrobe tab)." : "An outfit for the farmer (Wardrobe tab).")
            : trade.Result!.Fishing?.Description ?? "";
        CostText = "For " + Describe.Items(trade.Cost) + (trade.Coins > 0 ? $" and {Format.Number(trade.Coins)} coins" : "");
        Icon = cosmetic is null ? host.Sprites.ItemIcon(trade.Result!)
            : cosmetic.Slot == CosmeticSlot.Hat ? host.Sprites.Farmer("farmer_idle", -1, cosmetic.Id, Cosmetics.DefaultOutfit)
            : host.Sprites.Farmer("farmer_idle", -1, Cosmetics.DefaultHat, cosmetic.Id);
        MakeCommand = new RelayCommand(() => Make(1, done));
        MakeAllCommand = new RelayCommand(() => Make(long.MaxValue, done));
    }

    public TradeDef Trade { get; }
    public string Name { get; }
    public string Description { get; }
    public string CostText { get; }
    public ImageSource Icon { get; }
    public ICommand MakeCommand { get; }
    public ICommand MakeAllCommand { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string MakeAllText { get => _makeAllText; private set => Set(ref _makeAllText, value); }
    public bool CanMake { get => _canMake; private set => Set(ref _canMake, value); }
    public bool CanMakeMore { get => _canMakeMore; private set => Set(ref _canMakeMore, value); }

    private void Make(long times, Action<string> done)
    {
        long made = _host.Engine.Trade(Trade, times, out string reason);
        done(made <= 0 ? reason
            : Trade.Cosmetic is not null ? $"{Name} is yours! Wear it in the Wardrobe tab."
            : $"Got {Format.Number(made * Trade.ResultCount)} {Trade.Result!.Name}.");
    }

    public void Update()
    {
        var engine = _host.Engine;
        long max = engine.MaxTrades(Trade);
        CanMake = max > 0;
        CanMakeMore = max > 1;
        MakeAllText = $"All ({Format.Number(max)})";
        Status = CanMake ? "" : engine.CanTrade(Trade, out string reason) ? "" : reason;
    }
}

/// <summary>One line of "What's biting": an item, its chance, price and the level it needs.</summary>
public sealed class BiteRow : ObservableObject
{
    private readonly GameHost _host;
    private readonly TreasureEntry? _treasure;
    private string _chanceText = "";
    private double _rowOpacity = 1;

    public BiteRow(GameHost host, LakeDef lake, ItemDef item, TreasureEntry? treasure = null)
    {
        _host = host;
        Lake = lake;
        Item = item;
        _treasure = treasure;
        Icon = host.Sprites.ItemIcon(item);
        Kind = item.Kind switch
        {
            ItemKind.Fish => "Fish",
            ItemKind.LakeDrop => "Lake drop",
            ItemKind.Junk => "Junk",
            ItemKind.Treasure => treasure?.Great == true ? "Great treasure" : "Treasure",
            _ => "",
        };
    }

    public LakeDef Lake { get; }
    public ItemDef Item { get; }
    public ImageSource Icon { get; }
    public string Name => Item.Name;
    public string Kind { get; }
    public string ChanceText { get => _chanceText; private set => Set(ref _chanceText, value); }
    public double RowOpacity { get => _rowOpacity; private set => Set(ref _rowOpacity, value); }

    public void Update()
    {
        var engine = _host.Engine;
        string price = $"{Format.Number((long)engine.MarketPrice(Item))} coins";
        if (_treasure is not null)
        {
            double percent = engine.TreasureChances().First(t => t.Entry == _treasure).Percent;
            ChanceText = $"{Chance(percent)} · {price}";
            RowOpacity = 1;
            return;
        }
        var bite = engine.BiteChances(Lake).First(b => b.Entry.ItemId == Item.Id);
        ChanceText = bite.Unlocked ? $"{Chance(bite.Percent)} · {price}" : $"Fishing Level {bite.Entry.FishingLevel}";
        RowOpacity = bite.Unlocked ? 1 : 0.5;
    }

    private static string Chance(double percent) =>
        percent >= 10 ? $"{percent:0}%" : percent >= 1 ? $"{percent:0.#}%" : $"{percent:0.##}%";
}

/// <summary>One entry in the catch log: how many you've caught, or ??? if none yet.</summary>
public sealed class CatchLogRow : ObservableObject
{
    private readonly GameHost _host;
    private ImageSource? _icon;
    private string _name = "";
    private string _countText = "";

    public CatchLogRow(GameHost host, ItemDef item)
    {
        _host = host;
        Item = item;
    }

    public ItemDef Item { get; }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string CountText { get => _countText; private set => Set(ref _countText, value); }

    public void Update()
    {
        long caught = _host.Engine.TimesCaught(Item);
        Icon = caught > 0 ? _host.Sprites.ItemIcon(Item) : _host.Sprites.UnknownIcon(Item.Fishing!);
        Name = caught > 0 ? Item.Name : "???";
        CountText = caught > 0 ? $"x{Format.Number(caught)}" : "";
    }
}
