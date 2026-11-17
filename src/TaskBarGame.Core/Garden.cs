namespace TaskBarGame.Core;

public enum VisitorRarity { Common, Uncommon, Rare, Legendary }

/// <summary>
/// Someone who visits the garden and asks for crops. They prefer some crops, and their colours are
/// used to draw them (shirt, shirt shadow, hair: hex like "4A7BD1").
/// </summary>
public sealed record VisitorDef(
    string Id,
    string Name,
    VisitorRarity Rarity,
    string Greeting,
    IReadOnlyList<string> Favourites,
    int GardenLevel,
    string Shirt,
    string ShirtDark,
    string Hair);

/// <summary>A visitor waiting in the garden, what they want, and what they'll give for it.</summary>
public sealed class VisitorRequest
{
    public long Id { get; set; }
    public string VisitorId { get; set; } = "";
    public List<Ingredient> Wants { get; set; } = new();
    public long Coins { get; set; }
    public double FarmingXp { get; set; }
    public long GardenXp { get; set; }
    public long Tokens { get; set; }

    public VisitorDef Visitor => Garden.Visitor(VisitorId);
}

/// <summary>A timed boost from the Visitor Shop or the Composter's fertiliser.</summary>
public sealed record BoostDef(string Id, string Name, StatBonus Bonus, double Seconds, string Source);

/// <summary>A permanent upgrade bought with Compost, in levels.</summary>
/// <param name="ComposterSpeed">Percent faster composting per level (for the Composter's own upgrade).</param>
public sealed record ComposterUpgradeDef(
    string Id,
    string Name,
    string EffectPerLevel,
    int MaxLevel,
    int CostPerLevel,
    StatBonus PerLevel,
    int ComposterSpeed = 0)
{
    /// <summary>Compost for the next level: CostPerLevel × the level you're buying.</summary>
    public long CostFor(int level) => (long)CostPerLevel * level;
}

public enum ShopKind { Boost, Cosmetic, Seat, Compost }

/// <summary>Something the Visitor Shop sells for Garden Tokens.</summary>
/// <param name="Target">The boost or cosmetic id it gives (Boost and Cosmetic items).</param>
public sealed record ShopItem(string Id, string Name, string Description, long Cost, ShopKind Kind, string Target = "");

/// <summary>The garden's visitors, the Visitor Shop, the Composter and timed boosts: everything saved about them.</summary>
public sealed class GardenState
{
    public List<VisitorRequest> Visitors { get; set; } = new();
    public long NextVisitorId { get; set; } = 1;

    /// <summary>Seconds until the next visitor arrives (counts down while there's a free seat).</summary>
    public double NextVisitorSeconds { get; set; } = Garden.FirstVisitorSeconds;

    public long Tokens { get; set; }
    public long VisitorsServed { get; set; }

    /// <summary>Extra visitor seats bought in the shop.</summary>
    public int ExtraSeats { get; set; }

    /// <summary>Hats and outfits bought in the shop.</summary>
    public HashSet<string> OwnedCosmetics { get; set; } = new();

    /// <summary>Active timed boosts: boost id → seconds left.</summary>
    public Dictionary<string, double> Boosts { get; set; } = new();

    /// <summary>The Composter: matter waiting to become compost, compost made, and progress on the next one (seconds).</summary>
    public double Matter { get; set; }
    public long Compost { get; set; }
    public double CompostProgress { get; set; }

    /// <summary>Composter upgrade levels, by upgrade id.</summary>
    public Dictionary<string, int> Upgrades { get; set; } = new();

    public void Normalize()
    {
        Visitors ??= new();
        OwnedCosmetics ??= new();
        Boosts ??= new();
        Upgrades ??= new();
        Visitors.RemoveAll(v => v is null || Garden.Visitors.All(d => d.Id != v.VisitorId) || v.Wants is null
                                || v.Wants.Any(w => w is null || !Items.TryGet(w.ItemId, out _) || w.Count <= 0));
        NextVisitorId = Math.Max(NextVisitorId, Visitors.Select(v => v.Id).DefaultIfEmpty(0).Max() + 1);
        if (!double.IsFinite(NextVisitorSeconds) || NextVisitorSeconds < 0) NextVisitorSeconds = Garden.FirstVisitorSeconds;
        Tokens = Math.Max(0, Tokens);
        ExtraSeats = Math.Clamp(ExtraSeats, 0, Garden.MaxExtraSeats);
        foreach (var id in Boosts.Keys.ToList())
            if (Garden.FindBoost(id) is null || !double.IsFinite(Boosts[id]) || Boosts[id] <= 0) Boosts.Remove(id);
            else Boosts[id] = Math.Min(Boosts[id], Garden.MaxBoostSeconds);
        foreach (var id in Upgrades.Keys.ToList())
            if (Garden.FindUpgrade(id) is not { } upgrade) Upgrades.Remove(id);
            else Upgrades[id] = Math.Clamp(Upgrades[id], 0, upgrade.MaxLevel);
        Matter = double.IsFinite(Matter) ? Math.Clamp(Matter, 0, Garden.MaxMatter) : 0;
        Compost = Math.Max(0, Compost);
        if (!double.IsFinite(CompostProgress) || CompostProgress < 0) CompostProgress = 0;
    }
}

/// <summary>Everything about visitors, the Visitor Shop, the Composter and boosts that never changes.</summary>
public static class Garden
{
    // ------------------------------------------------------------------ visitors

    public const int VisitorsGardenLevel = 2;
    public const double VisitorIntervalSeconds = 15 * 60;
    public const double FirstVisitorSeconds = 60;
    public const int BaseSeats = 3;
    public const int MaxExtraSeats = 3;

    public static readonly IReadOnlyList<VisitorDef> Visitors = new VisitorDef[]
    {
        new("bramble", "Old Bramble", VisitorRarity.Common, "Back in my day, wheat grew taller than this fence.",
            new[] { "wheat", "potato" }, 2, "8A6A3A", "5C4630", "D8D8D8"),
        new("mira", "Mira the Baker", VisitorRarity.Common, "I need flour for a hundred loaves by sundown!",
            new[] { "wheat", "pumpkin", "sugarcane" }, 2, "F2F2F2", "C8C0B4", "8A4A2A"),
        new("pip", "Pip", VisitorRarity.Common, "Carrots make you see in the dark. Probably.",
            new[] { "carrot" }, 2, "E8B923", "A87B16", "C4703A"),
        new("hollis", "Hollis the Miller", VisitorRarity.Common, "My mill's been quiet all week.",
            new[] { "wheat", "potato", "carrot" }, 2, "5A7A9A", "3A5470", "4A3222"),
        new("juniper", "Juniper the Herbalist", VisitorRarity.Uncommon, "Every plant is a medicine, if you know how.",
            new[] { "mushroom", "cactus", "carrot" }, 3, "4A9E3F", "2E6B2E", "A97FDC"),
        new("rowan", "Rowan the Ranger", VisitorRarity.Uncommon, "Long trail ahead. I'll need supplies.",
            new[] { "melon", "carrot", "potato" }, 4, "6E5A2E", "4A3C1E", "2A1E17"),
        new("sable", "Sable the Trader", VisitorRarity.Uncommon, "I buy in bulk. Packed, if you please.",
            new[] { "sugarcane", "pumpkin", "cocoa" }, 5, "9E2A22", "6E1A16", "1B1B1B"),
        new("gale", "Captain Gale", VisitorRarity.Rare, "A long voyage needs fresh food for the crew.",
            new[] { "melon", "sugarcane", "cocoa" }, 6, "2C4F8F", "1E365F", "F2F2F2"),
        new("fern", "Fern the Florist", VisitorRarity.Rare, "Pumpkins for the fair, and something rare for my window.",
            new[] { "pumpkin", "moonbloom", "melon" }, 7, "F06A9E", "B04070", "F2D36B"),
        new("orrin", "Orrin the Stargazer", VisitorRarity.Legendary, "The stars say your field holds something special.",
            new[] { "moonbloom", "mushroom", "cocoa" }, 9, "3B2F6B", "251D45", "B0B0B0"),
    };

    public static VisitorDef Visitor(string id) => Visitors.FirstOrDefault(v => v.Id == id) ?? Visitors[0];

    /// <summary>How often each rarity turns up (out of 100).</summary>
    public static int Weight(VisitorRarity rarity) => rarity switch
    {
        VisitorRarity.Common => 60,
        VisitorRarity.Uncommon => 25,
        VisitorRarity.Rare => 12,
        _ => 3,
    };

    /// <summary>Rarer visitors want more and pay more.</summary>
    public static double RarityScale(VisitorRarity rarity) => rarity switch
    {
        VisitorRarity.Common => 1,
        VisitorRarity.Uncommon => 1.5,
        VisitorRarity.Rare => 2.2,
        _ => 3.5,
    };

    public static long RarityTokens(VisitorRarity rarity) => rarity switch
    {
        VisitorRarity.Common => 2,
        VisitorRarity.Uncommon => 4,
        VisitorRarity.Rare => 8,
        _ => 16,
    };

    /// <summary>Visitors pay this much more than the Market would for what they ask.</summary>
    public const double CoinBonus = 1.5;

    // ------------------------------------------------------------------ boosts

    public const double MaxBoostSeconds = 3 * 60 * 60;

    public static readonly IReadOnlyList<BoostDef> Boosts = new BoostDef[]
    {
        new("fertiliser", "Fertiliser", new StatBonus(Fortune: 30), 20 * 60, "Composter"),
        new("lucky_seeds", "Lucky Seeds", new StatBonus(Fortune: 40), 30 * 60, "Visitor Shop"),
        new("swift_tonic", "Swift Tonic", new StatBonus(Speed: 15), 30 * 60, "Visitor Shop"),
        new("scholar_tea", "Scholar's Tea", new StatBonus(Xp: 25), 30 * 60, "Visitor Shop"),
    };

    public static BoostDef? FindBoost(string? id) => Boosts.FirstOrDefault(b => b.Id == id);

    // ------------------------------------------------------------------ the Visitor Shop

    public static readonly IReadOnlyList<ShopItem> Shop = new ShopItem[]
    {
        new("lucky_seeds", "Lucky Seeds", "+40 Harvest Fortune for 30 minutes", 6, ShopKind.Boost, "lucky_seeds"),
        new("swift_tonic", "Swift Tonic", "+15% Farming Speed for 30 minutes", 6, ShopKind.Boost, "swift_tonic"),
        new("scholar_tea", "Scholar's Tea", "+25% Farming XP for 30 minutes", 5, ShopKind.Boost, "scholar_tea"),
        new("compost_bag", "Bag of Compost", "5 Compost for the Composter's upgrades", 8, ShopKind.Compost),
        new("extra_bench", "Garden Bench", "Room for one more visitor (up to 3 more)", 15, ShopKind.Seat),
        new("hood", "Traveller's Hood", "A hat for the Wardrobe", 20, ShopKind.Cosmetic, "hood"),
        new("wanderer", "Wanderer's Garb", "An outfit for the Wardrobe", 35, ShopKind.Cosmetic, "wanderer"),
        new("starhat", "Stargazer's Hat", "A hat for the Wardrobe", 50, ShopKind.Cosmetic, "starhat"),
    };

    public const long CompostPerBag = 5;

    // ------------------------------------------------------------------ the Composter

    public const int ComposterGardenLevel = 3;

    /// <summary>Matter (the Market value of the crops you put in) needed for one Compost.</summary>
    public const double MatterPerCompost = 4_000;

    /// <summary>Seconds to make one Compost (faster with the Busy Composter upgrade).</summary>
    public const double SecondsPerCompost = 10 * 60;

    /// <summary>The Composter holds at most this much matter.</summary>
    public const double MaxMatter = 400_000;

    /// <summary>Compost one fertiliser costs.</summary>
    public const long FertiliserCost = 3;

    public static readonly IReadOnlyList<ComposterUpgradeDef> Upgrades = new ComposterUpgradeDef[]
    {
        new("rich_soil", "Rich Soil", "+4 Harvest Fortune for every crop", 10, 3, new StatBonus(Fortune: 4)),
        new("quick_steps", "Quick Steps", "+2% Farming Speed", 5, 5, new StatBonus(Speed: 2)),
        new("wise_roots", "Wise Roots", "+2% Farming XP", 5, 4, new StatBonus(Xp: 2)),
        new("busy_composter", "Busy Composter", "The Composter works 10% faster", 5, 4, new StatBonus(), ComposterSpeed: 10),
    };

    public static ComposterUpgradeDef? FindUpgrade(string? id) => Upgrades.FirstOrDefault(u => u.Id == id);

    /// <summary>Rounds to 2 significant figures so amounts look tidy (1,234 → 1,200).</summary>
    internal static long RoundNice(double value)
    {
        if (value < 100) return Math.Max(1, (long)Math.Round(value));
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
        return (long)(Math.Round(value / magnitude) * magnitude);
    }
}

/// <summary>Visitors, the Visitor Shop, the Composter and timed boosts.</summary>
public sealed partial class GameEngine
{
    public event Action<VisitorRequest>? VisitorArrived;

    // ================================================================= visitors

    public bool VisitorsUnlocked => GardenLevel >= Garden.VisitorsGardenLevel;
    public int VisitorSeats => Garden.BaseSeats + State.Garden.ExtraSeats;
    public IReadOnlyList<VisitorRequest> Visitors => State.Garden.Visitors;

    /// <summary>Whether you can give a visitor what they want (and if not, why).</summary>
    public bool CanServe(VisitorRequest request, out string reason)
    {
        foreach (var want in request.Wants)
        {
            var item = want.Item;
            if (IsLocked(item))
            {
                reason = $"{item.Name} is locked";
                return false;
            }
            long have = GetItem(item);
            if (have < want.Count)
            {
                reason = $"Needs {want.Count - have:N0} more {item.Name}";
                return false;
            }
        }
        reason = "";
        return true;
    }

    /// <summary>Gives a visitor what they asked for and collects the reward.</summary>
    public bool ServeVisitor(long id, out string reason)
    {
        var request = State.Garden.Visitors.FirstOrDefault(v => v.Id == id);
        if (request is null)
        {
            reason = "That visitor has gone";
            return false;
        }
        if (!CanServe(request, out reason)) return false;

        int farmingBefore = FarmingLevel, gardenBefore = GardenLevel;
        foreach (var want in request.Wants) AddItem(want.Item, -want.Count);
        State.Coins += request.Coins;
        State.FarmingXp += request.FarmingXp;
        State.GardenXp += request.GardenXp;
        State.Garden.Tokens += request.Tokens;
        State.Garden.VisitorsServed++;
        State.Garden.Visitors.Remove(request);

        if (FarmingLevel > farmingBefore) OnFarmingLevelUp(farmingBefore, FarmingLevel);
        if (GardenLevel > gardenBefore) OnGardenLevelUp(gardenBefore, GardenLevel);
        RaiseChanged();
        return true;
    }

    /// <summary>Sends a visitor away without trading.</summary>
    public bool RefuseVisitor(long id)
    {
        int removed = State.Garden.Visitors.RemoveAll(v => v.Id == id);
        if (removed > 0) RaiseChanged();
        return removed > 0;
    }

    /// <summary>Lets the visitor clock run: a new visitor arrives every 15 minutes while there's a free seat.</summary>
    private int VisitorTick(double seconds)
    {
        if (!VisitorsUnlocked) return 0;
        var garden = State.Garden;
        int arrived = 0;
        while (seconds > 0 && garden.Visitors.Count < VisitorSeats)
        {
            if (seconds < garden.NextVisitorSeconds)
            {
                garden.NextVisitorSeconds -= seconds;
                break;
            }
            seconds -= garden.NextVisitorSeconds;
            garden.NextVisitorSeconds = Garden.VisitorIntervalSeconds;
            var request = NewVisitorRequest();
            garden.Visitors.Add(request);
            arrived++;
            if (!_quiet) VisitorArrived?.Invoke(request);
        }
        if (arrived > 0) RaiseChanged();
        return arrived;
    }

    /// <summary>
    /// Picks a visitor who isn't already waiting and works out what they want and pay
    /// (it doesn't seat them: the visitor clock does that).
    /// </summary>
    public VisitorRequest NewVisitorRequest()
    {
        var waiting = State.Garden.Visitors.Select(v => v.VisitorId).ToHashSet();
        var candidates = Garden.Visitors.Where(v => v.GardenLevel <= GardenLevel && !waiting.Contains(v.Id)).ToList();
        if (candidates.Count == 0) candidates = Garden.Visitors.Where(v => v.GardenLevel <= GardenLevel).ToList();
        if (candidates.Count == 0) candidates = Garden.Visitors.Take(1).ToList();

        int total = candidates.Sum(v => Garden.Weight(v.Rarity));
        int roll = _rng.Next(total);
        var visitor = candidates[^1];
        foreach (var candidate in candidates)
        {
            roll -= Garden.Weight(candidate.Rarity);
            if (roll < 0)
            {
                visitor = candidate;
                break;
            }
        }

        // What they want: worth about 300 coins per Garden level at the Market (more for rarer visitors),
        // split over one or two of their favourite crops (any unlocked crop if none of those are).
        var crops = visitor.Favourites.Select(Crops.Get).Where(IsCropUnlocked).ToList();
        if (crops.Count == 0) crops = UnlockedCrops.ToList();
        int itemCount = visitor.Rarity >= VisitorRarity.Rare && crops.Count > 1 ? 2 : 1;
        double value = 300 * (1 + GardenLevel) * Garden.RarityScale(visitor.Rarity);

        var wants = new List<Ingredient>();
        foreach (var crop in crops.OrderBy(_ => _rng.Next()).Take(itemCount))
        {
            // From Garden Level 6, big requests are for Packed crops.
            double amount = value / itemCount / crop.SellPrice;
            bool packed = GardenLevel >= 6 && amount >= 4 * ItemDef.PackSize;
            var item = packed ? Items.Packed(crop) : Items.Raw(crop);
            long count = packed
                ? Math.Max(1, (long)Math.Round(amount / ItemDef.PackSize))
                : Math.Max(10, Garden.RoundNice(amount));
            wants.Add(new Ingredient(item.Id, count));
        }

        double worth = wants.Sum(w => w.Count * w.Item.SellPrice);
        return new VisitorRequest
        {
            Id = State.Garden.NextVisitorId++,
            VisitorId = visitor.Id,
            Wants = wants,
            Coins = (long)Math.Round(worth * Garden.CoinBonus),
            FarmingXp = Math.Round(worth / 4),
            GardenXp = (long)Math.Round(60 * (1 + (int)visitor.Rarity) * (1 + GardenLevel / 4.0)),
            Tokens = Garden.RarityTokens(visitor.Rarity),
        };
    }

    // ================================================================= the Visitor Shop

    public long Tokens => State.Garden.Tokens;

    /// <summary>The Garden Bench gets dearer each time: 15, 30, then 60 tokens.</summary>
    public long ShopPrice(ShopItem item) =>
        item.Kind == ShopKind.Seat ? item.Cost << Math.Min(State.Garden.ExtraSeats, 10) : item.Cost;

    public bool CanBuy(ShopItem item, out string reason)
    {
        if (!VisitorsUnlocked)
        {
            reason = $"Needs Garden Level {Garden.VisitorsGardenLevel}";
            return false;
        }
        if (item.Kind == ShopKind.Seat && State.Garden.ExtraSeats >= Garden.MaxExtraSeats)
        {
            reason = "All the benches are out";
            return false;
        }
        if (item.Kind == ShopKind.Cosmetic && State.Garden.OwnedCosmetics.Contains(item.Target))
        {
            reason = "Already yours";
            return false;
        }
        if (item.Kind == ShopKind.Boost && BoostSecondsLeft(item.Target) >= Garden.MaxBoostSeconds - 60)
        {
            reason = "Already as long as it goes";
            return false;
        }
        long price = ShopPrice(item);
        if (State.Garden.Tokens < price)
        {
            reason = $"Needs {price - State.Garden.Tokens:N0} more tokens";
            return false;
        }
        reason = "";
        return true;
    }

    public bool Buy(ShopItem item, out string reason)
    {
        if (!CanBuy(item, out reason)) return false;
        State.Garden.Tokens -= ShopPrice(item);
        switch (item.Kind)
        {
            case ShopKind.Boost:
                StartBoost(item.Target);
                break;
            case ShopKind.Cosmetic:
                State.Garden.OwnedCosmetics.Add(item.Target);
                break;
            case ShopKind.Seat:
                State.Garden.ExtraSeats++;
                break;
            case ShopKind.Compost:
                State.Garden.Compost += Garden.CompostPerBag;
                break;
        }
        RaiseChanged();
        return true;
    }

    // ================================================================= boosts

    public double BoostSecondsLeft(string id) => State.Garden.Boosts.GetValueOrDefault(id);

    /// <summary>Boosts running now, with seconds left.</summary>
    public IEnumerable<(BoostDef Boost, double SecondsLeft)> ActiveBoosts =>
        Garden.Boosts.Where(b => BoostSecondsLeft(b.Id) > 0).Select(b => (b, BoostSecondsLeft(b.Id)));

    /// <summary>Starts a boost, or adds its time to the one already running (up to 3 hours).</summary>
    private void StartBoost(string id)
    {
        if (Garden.FindBoost(id) is not { } boost) return;
        State.Garden.Boosts[id] = Math.Min(Garden.MaxBoostSeconds, BoostSecondsLeft(id) + boost.Seconds);
    }

    private void BoostTick(double seconds)
    {
        var boosts = State.Garden.Boosts;
        foreach (var id in boosts.Keys.ToList())
        {
            boosts[id] -= seconds;
            if (boosts[id] <= 0) boosts.Remove(id);
        }
    }

    /// <summary>Permanent Composter upgrades plus running boosts.</summary>
    public StatBonus GardenBonus
    {
        get
        {
            var bonus = new StatBonus();
            foreach (var upgrade in Garden.Upgrades)
                for (int level = 0; level < UpgradeLevel(upgrade); level++) bonus += upgrade.PerLevel;
            foreach (var (boost, _) in ActiveBoosts) bonus += boost.Bonus;
            return bonus;
        }
    }

    // ================================================================= the Composter

    public bool ComposterUnlocked => GardenLevel >= Garden.ComposterGardenLevel;
    public double Matter => State.Garden.Matter;
    public long Compost => State.Garden.Compost;

    /// <summary>Matter one of an item adds: its Market value (without your sell bonus).</summary>
    public static double MatterOf(ItemDef item) => item.SellPrice;

    /// <summary>Seconds per Compost, with the Busy Composter upgrade.</summary>
    public double SecondsPerCompost =>
        Garden.SecondsPerCompost / (1 + UpgradeLevel(Garden.FindUpgrade("busy_composter")!) * 0.10);

    /// <summary>How many of an item fit in the Composter right now.</summary>
    public long ComposterRoom(ItemDef item) => (long)Math.Floor((Garden.MaxMatter - Matter) / MatterOf(item));

    /// <summary>Puts items in the Composter (locked items can't go in). Returns how many went in.</summary>
    public long AddToComposter(string itemId, long count, out string reason)
    {
        reason = "";
        if (!Items.TryGet(itemId, out var item) || count <= 0) return 0;
        if (!ComposterUnlocked)
        {
            reason = $"Needs Garden Level {Garden.ComposterGardenLevel}";
            return 0;
        }
        if (IsLocked(item))
        {
            reason = $"{item.Name} is locked";
            return 0;
        }
        long added = Math.Min(Math.Min(count, GetItem(item)), ComposterRoom(item));
        if (added <= 0)
        {
            reason = GetItem(item) <= 0 ? $"You have no {item.Name}" : "The Composter is full";
            return 0;
        }
        AddItem(item, -added);
        State.Garden.Matter += added * MatterOf(item);
        RaiseChanged();
        return added;
    }

    /// <summary>
    /// Puts every plain crop you could sell to the Market (not locked, not kept for your next upgrades)
    /// in the Composter. Returns how many crops went in.
    /// </summary>
    public long AddSpareCropsToComposter()
    {
        long total = 0;
        bool quiet = _quiet;
        _quiet = true;
        try
        {
            foreach (var crop in Crops.All)
            {
                var raw = Items.Raw(crop);
                long spare = SellableAmount(raw);
                if (spare > 0) total += AddToComposter(raw.Id, spare, out _);
            }
        }
        finally
        {
            _quiet = quiet;
        }
        if (total > 0) RaiseChanged();
        return total;
    }

    private void CompostTick(double seconds)
    {
        var garden = State.Garden;
        if (garden.Matter < Garden.MatterPerCompost)
        {
            garden.CompostProgress = 0;
            return;
        }
        garden.CompostProgress += seconds;
        double each = SecondsPerCompost;
        bool made = false;
        while (garden.CompostProgress >= each && garden.Matter >= Garden.MatterPerCompost)
        {
            garden.CompostProgress -= each;
            garden.Matter -= Garden.MatterPerCompost;
            garden.Compost++;
            made = true;
        }
        if (garden.Matter < Garden.MatterPerCompost) garden.CompostProgress = 0;
        if (made) RaiseChanged();
    }

    public int UpgradeLevel(ComposterUpgradeDef upgrade) => State.Garden.Upgrades.GetValueOrDefault(upgrade.Id);

    public bool CanBuyUpgrade(ComposterUpgradeDef upgrade, out string reason)
    {
        int level = UpgradeLevel(upgrade);
        if (!ComposterUnlocked)
        {
            reason = $"Needs Garden Level {Garden.ComposterGardenLevel}";
            return false;
        }
        if (level >= upgrade.MaxLevel)
        {
            reason = "Maxed";
            return false;
        }
        long cost = upgrade.CostFor(level + 1);
        if (Compost < cost)
        {
            reason = $"Needs {cost - Compost:N0} more Compost";
            return false;
        }
        reason = "";
        return true;
    }

    public bool BuyUpgrade(ComposterUpgradeDef upgrade, out string reason)
    {
        if (!CanBuyUpgrade(upgrade, out reason)) return false;
        int level = UpgradeLevel(upgrade) + 1;
        State.Garden.Compost -= upgrade.CostFor(level);
        State.Garden.Upgrades[upgrade.Id] = level;
        RaiseChanged();
        return true;
    }

    /// <summary>Spends Compost on fertiliser: +30 Harvest Fortune for 20 minutes (adds to the time left).</summary>
    public bool Fertilise(out string reason)
    {
        if (!ComposterUnlocked)
        {
            reason = $"Needs Garden Level {Garden.ComposterGardenLevel}";
            return false;
        }
        if (Compost < Garden.FertiliserCost)
        {
            reason = $"Needs {Garden.FertiliserCost - Compost:N0} more Compost";
            return false;
        }
        if (BoostSecondsLeft("fertiliser") >= Garden.MaxBoostSeconds - 60)
        {
            reason = "The field can't take any more for now";
            return false;
        }
        State.Garden.Compost -= Garden.FertiliserCost;
        StartBoost("fertiliser");
        reason = "";
        RaiseChanged();
        return true;
    }
}
