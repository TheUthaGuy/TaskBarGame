using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>The Visitors tab (visitors and the Visitor Shop) and the Composter in the Garden tab.</summary>
public sealed partial class PanelViewModel
{
    private string _tokensText = "";
    private string _visitorsInfo = "";
    private bool _visitorsUnlocked;
    private bool _hasVisitors;
    private string _gardenMessage = "";
    private bool _composterUnlocked;
    private string _compostText = "";
    private string _matterText = "";
    private double _compostProgress;
    private string _nextCompostText = "";
    private string _fertiliseText = "";
    private bool _canFertilise;
    private string _boostsText = "";

    // ---------------------------------------------------------------- Visitors tab

    public ObservableCollection<VisitorRow> VisitorRows { get; } = new();
    public IReadOnlyList<ShopRow> ShopRows { get; private set; } = Array.Empty<ShopRow>();
    public string TokensText { get => _tokensText; private set => Set(ref _tokensText, value); }
    public string VisitorsInfo { get => _visitorsInfo; private set => Set(ref _visitorsInfo, value); }
    public bool VisitorsUnlocked { get => _visitorsUnlocked; private set => Set(ref _visitorsUnlocked, value); }
    public bool HasVisitors { get => _hasVisitors; private set => Set(ref _hasVisitors, value); }

    /// <summary>What just happened in the Visitors tab or the Composter ("Served Old Bramble...").</summary>
    public string GardenMessage { get => _gardenMessage; private set => Set(ref _gardenMessage, value); }

    // ---------------------------------------------------------------- Garden tab: the Composter

    public bool ComposterUnlocked { get => _composterUnlocked; private set => Set(ref _composterUnlocked, value); }
    public string ComposterLockedText => $"The Composter opens at Garden Level {Garden.ComposterGardenLevel}.";
    public string CompostText { get => _compostText; private set => Set(ref _compostText, value); }
    public string MatterText { get => _matterText; private set => Set(ref _matterText, value); }
    public double CompostProgress { get => _compostProgress; private set => Set(ref _compostProgress, value); }
    public string NextCompostText { get => _nextCompostText; private set => Set(ref _nextCompostText, value); }
    public string FertiliseText { get => _fertiliseText; private set => Set(ref _fertiliseText, value); }
    public bool CanFertilise { get => _canFertilise; private set => Set(ref _canFertilise, value); }
    public IReadOnlyList<ComposterUpgradeRow> ComposterUpgrades { get; private set; } = Array.Empty<ComposterUpgradeRow>();
    public ICommand AddSpareToComposterCommand { get; private set; } = null!;
    public ICommand FertiliseCommand { get; private set; } = null!;

    /// <summary>"Lucky Seeds +40 Fortune (23m left) · Fertiliser +30 Fortune (8m left)", or "".</summary>
    public string BoostsText { get => _boostsText; private set => Set(ref _boostsText, value); }

    private void InitGarden()
    {
        ShopRows = Garden.Shop.Select(item => new ShopRow(_host, item, message =>
        {
            GardenMessage = message;
            Refresh();
        })).ToList();
        ComposterUpgrades = Garden.Upgrades.Select(u => new ComposterUpgradeRow(_host, u, message =>
        {
            GardenMessage = message;
            Refresh();
        })).ToList();

        AddSpareToComposterCommand = new RelayCommand(() =>
        {
            long added = Engine.AddSpareCropsToComposter();
            GardenMessage = added > 0
                ? $"Put {Format.Number(added)} spare crops in the Composter."
                : "No spare crops: locked crops and what your next upgrades need are kept.";
            _host.Save();
            Refresh();
        });
        FertiliseCommand = new RelayCommand(() =>
        {
            GardenMessage = Engine.Fertilise(out string reason) ? "Fertilised! +30 Harvest Fortune for 20 minutes." : reason;
            _host.Save();
            Refresh();
        });
    }

    /// <summary>Puts all of one item in the Composter (from the Add ▾ menu).</summary>
    public void AddToComposter(ItemDef item)
    {
        long added = Engine.AddToComposter(item.Id, long.MaxValue, out string reason);
        GardenMessage = added > 0 ? $"Put {Format.Number(added)} {item.Name} in the Composter." : reason;
        _host.Save();
        Refresh();
    }

    /// <summary>Items you could put in the Composter, for the Add ▾ menu.</summary>
    public IEnumerable<(ItemDef Item, long Count)> ComposterChoices =>
        Items.All.Where(i => Engine.GetItem(i) > 0 && !Engine.IsLocked(i)).Select(i => (i, Engine.GetItem(i)));

    private void RefreshGarden()
    {
        var garden = Engine.State.Garden;

        // ---- visitors
        VisitorsUnlocked = Engine.VisitorsUnlocked;
        TokensText = Format.Number(Engine.Tokens);
        if (!VisitorsUnlocked)
        {
            VisitorsInfo = $"Visitors start coming at Garden Level {Garden.VisitorsGardenLevel}.";
        }
        else
        {
            string next = Engine.Visitors.Count >= Engine.VisitorSeats
                ? "Every seat is taken: serve or send someone off to make room."
                : $"Next visitor in about {Format.Duration(garden.NextVisitorSeconds)}.";
            VisitorsInfo = $"{Engine.Visitors.Count} of {Engine.VisitorSeats} seats taken · {next} "
                           + $"Visitors served: {Format.Number(garden.VisitorsServed)}.";
        }

        var current = Engine.Visitors.ToList();
        if (!current.Select(v => v.Id).SequenceEqual(VisitorRows.Select(r => r.Request.Id)))
        {
            VisitorRows.Clear();
            foreach (var request in current)
                VisitorRows.Add(new VisitorRow(_host, request, message =>
                {
                    GardenMessage = message;
                    Refresh();
                }));
        }
        foreach (var row in VisitorRows) row.Update();
        HasVisitors = VisitorRows.Count > 0;
        foreach (var row in ShopRows) row.Update();

        // ---- the Composter
        ComposterUnlocked = Engine.ComposterUnlocked;
        CompostText = $"{Format.Number(Engine.Compost)} Compost";
        MatterText = $"{Format.Number((long)Engine.Matter)} matter inside (up to {Format.Number((long)Garden.MaxMatter)})";
        if (Engine.Matter >= Garden.MatterPerCompost)
        {
            double each = Engine.SecondsPerCompost;
            CompostProgress = Math.Clamp(garden.CompostProgress / each, 0, 1);
            NextCompostText = $"Next Compost in {Format.Duration(each - garden.CompostProgress)} · "
                              + $"{Format.Number((long)(Engine.Matter / Garden.MatterPerCompost))} more to come from what's inside";
        }
        else
        {
            CompostProgress = Engine.Matter / Garden.MatterPerCompost;
            NextCompostText = $"Add {Format.Number((long)Math.Ceiling(Garden.MatterPerCompost - Engine.Matter))} more matter "
                              + "to make Compost. Each crop adds its Market value.";
        }
        double left = Engine.BoostSecondsLeft("fertiliser");
        FertiliseText = $"Fertilise ({Garden.FertiliserCost} Compost)";
        CanFertilise = Engine.Compost >= Garden.FertiliserCost && left < Garden.MaxBoostSeconds - 60;
        foreach (var row in ComposterUpgrades) row.Update();

        var boosts = Engine.ActiveBoosts.Select(b => $"{b.Boost.Name}: {b.Boost.Bonus.Describe()} ({Format.Duration(b.SecondsLeft)} left)").ToList();
        BoostsText = boosts.Count == 0 ? "" : "Running now: " + string.Join(" · ", boosts);
    }
}

/// <summary>A visitor waiting in the garden.</summary>
public sealed class VisitorRow : ObservableObject
{
    private static readonly Dictionary<VisitorRarity, Brush> RarityBrushes = new()
    {
        [VisitorRarity.Common] = Format.Brush("#A3B19F"),
        [VisitorRarity.Uncommon] = Format.Brush("#7BC950"),
        [VisitorRarity.Rare] = Format.Brush("#5FD4E8"),
        [VisitorRarity.Legendary] = Format.Brush("#F5C542"),
    };

    private readonly GameHost _host;
    private string _wantsText = "";
    private string _status = "";
    private bool _canServe;
    private bool _hasMissing;
    private bool _canBuyMissing;
    private string _buyMissingText = "";

    public VisitorRow(GameHost host, VisitorRequest request, Action<string> done)
    {
        _host = host;
        Request = request;
        Portrait = host.Sprites.Visitor(request.Visitor);
        RarityBrush = RarityBrushes[request.Visitor.Rarity];
        long id = request.Id;
        string name = request.Visitor.Name;
        ServeCommand = new RelayCommand(() =>
        {
            done(host.Engine.ServeVisitor(id, out string reason)
                ? $"{name} is delighted! +{Format.Coins(request.Coins)}, +{Format.Number(request.Tokens)} tokens."
                : reason);
            host.Save();
        });
        RefuseCommand = new RelayCommand(() =>
        {
            host.Engine.RefuseVisitor(id);
            done($"{name} wanders off.");
            host.Save();
        });
        BuyMissingCommand = new RelayCommand(() =>
        {
            long spent = host.Engine.BuyMissingForVisitor(id, out string reason);
            done(spent > 0 ? $"Bought what {name} wanted on the Bazaar for {Format.Coins(spent)}." : reason);
            host.Save();
        });
    }

    public VisitorRequest Request { get; }
    public ImageSource Portrait { get; }
    public string Name => Request.Visitor.Name;
    public string Rarity => Request.Visitor.Rarity.ToString();
    public Brush RarityBrush { get; }
    public string Greeting => $"“{Request.Visitor.Greeting}”";
    public string RewardText =>
        $"Gives {Format.Coins(Request.Coins)} · {Format.Number((long)Request.FarmingXp)} Farming XP · "
        + $"{Format.Number(Request.GardenXp)} Garden XP · {Format.Number(Request.Tokens)} tokens";
    public ICommand ServeCommand { get; }
    public ICommand RefuseCommand { get; }
    public ICommand BuyMissingCommand { get; }
    public string WantsText { get => _wantsText; private set => Set(ref _wantsText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanServe { get => _canServe; private set => Set(ref _canServe, value); }
    public bool HasMissing { get => _hasMissing; private set => Set(ref _hasMissing, value); }
    public bool CanBuyMissing { get => _canBuyMissing; private set => Set(ref _canBuyMissing, value); }
    public string BuyMissingText { get => _buyMissingText; private set => Set(ref _buyMissingText, value); }

    public void Update()
    {
        var engine = _host.Engine;
        WantsText = "Wants " + string.Join(" and ", Request.Wants.Select(w =>
            $"{Format.Number(w.Count)} {w.Item.Name} (you have {Format.Number(engine.GetItem(w.Item))})"));
        CanServe = engine.CanServe(Request, out string reason);
        Status = CanServe ? "You have everything!" : reason;

        var missing = engine.MissingItems(Request.Wants);
        HasMissing = missing.Count > 0;
        if (HasMissing)
        {
            long cost = engine.BazaarCost(missing);
            CanBuyMissing = engine.State.Coins >= cost;
            BuyMissingText = $"Buy missing · {Format.Number(cost)}";
        }
    }
}

/// <summary>Something in the Visitor Shop.</summary>
public sealed class ShopRow : ObservableObject
{
    private readonly GameHost _host;
    private string _priceText = "";
    private string _status = "";
    private bool _canBuy;
    private ImageSource? _icon;

    public ShopRow(GameHost host, ShopItem item, Action<string> done)
    {
        _host = host;
        Item = item;
        BuyCommand = new RelayCommand(() =>
        {
            done(host.Engine.Buy(item, out string reason) ? $"Bought {item.Name}." : reason);
            host.Save();
        });
    }

    public ShopItem Item { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public ICommand BuyCommand { get; }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string PriceText { get => _priceText; private set => Set(ref _priceText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanBuy { get => _canBuy; private set => Set(ref _canBuy, value); }

    public void Update()
    {
        var engine = _host.Engine;
        PriceText = $"{Format.Number(engine.ShopPrice(Item))} tokens";
        CanBuy = engine.CanBuy(Item, out string reason);
        Status = CanBuy ? "" : reason;
        if (Item.Kind == ShopKind.Boost && engine.BoostSecondsLeft(Item.Target) > 0)
            Status = $"Running: {Format.Duration(engine.BoostSecondsLeft(Item.Target))} left (buy more to add time)";

        var hat = Cosmetics.WornHat(engine).Id;
        var outfit = Cosmetics.WornOutfit(engine).Id;
        Icon = Item.Kind switch
        {
            ShopKind.Cosmetic when Cosmetics.Find(Item.Target) is { Slot: CosmeticSlot.Hat } =>
                _host.Sprites.Farmer("farmer_idle", -1, Item.Target, outfit),
            ShopKind.Cosmetic => _host.Sprites.Farmer("farmer_idle", -1, hat, Item.Target),
            ShopKind.Seat => _host.Sprites["visitor"],
            ShopKind.Compost => _host.Sprites["crate"],
            _ => _host.Sprites["sparkle"],
        };
    }
}

/// <summary>A permanent upgrade bought with Compost.</summary>
public sealed class ComposterUpgradeRow : ObservableObject
{
    private readonly GameHost _host;
    private string _levelText = "";
    private string _buyText = "";
    private string _status = "";
    private bool _canBuy;

    public ComposterUpgradeRow(GameHost host, ComposterUpgradeDef upgrade, Action<string> done)
    {
        _host = host;
        Upgrade = upgrade;
        BuyCommand = new RelayCommand(() =>
        {
            done(host.Engine.BuyUpgrade(upgrade, out string reason)
                ? $"{upgrade.Name} is now level {host.Engine.UpgradeLevel(upgrade)}."
                : reason);
            host.Save();
        });
    }

    public ComposterUpgradeDef Upgrade { get; }
    public string Name => Upgrade.Name;
    public string Effect => Upgrade.EffectPerLevel + " per level";
    public ICommand BuyCommand { get; }
    public string LevelText { get => _levelText; private set => Set(ref _levelText, value); }
    public string BuyText { get => _buyText; private set => Set(ref _buyText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanBuy { get => _canBuy; private set => Set(ref _canBuy, value); }

    public void Update()
    {
        var engine = _host.Engine;
        int level = engine.UpgradeLevel(Upgrade);
        LevelText = $"Level {level} / {Upgrade.MaxLevel}";
        CanBuy = engine.CanBuyUpgrade(Upgrade, out string reason);
        BuyText = level >= Upgrade.MaxLevel ? "Maxed" : $"Buy · {Format.Number(Upgrade.CostFor(level + 1))} Compost";
        Status = CanBuy || level >= Upgrade.MaxLevel ? "" : reason;
    }
}
