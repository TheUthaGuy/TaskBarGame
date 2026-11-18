namespace TaskBarGame.Core;

// =====================================================================================================
// Fishing: the farmer stands on a pontoon above a lake and fishes by themselves. This file is the
// data (items, lakes, what bites, rods, fishing armour, bait and trades); FishingEngine.cs has the rules.
// Change any number here to rebalance fishing.
// =====================================================================================================

/// <summary>Every fish, lake drop, treasure, junk and bait in the game.</summary>
public static class FishingItems
{
    // Fish are drawn from a few shapes ("fish", "fish_long", "eel", "catfish", "swordfish") and
    // recoloured: body, body dark, belly, fins.
    private static FishingItemDef Fish(string id, string name, long price, string shape, string colour) =>
        new(id, name, ItemKind.Fish, price, shape, colour);

    // ---------------------------------------------------------------- fish of Willow Lake
    public static readonly FishingItemDef Perch = Fish("perch", "Perch", 40, "fish", "#8DB255");
    public static readonly FishingItemDef Carp = Fish("carp", "Carp", 50, "fish", "#C9A24A");
    public static readonly FishingItemDef Trout = Fish("trout", "Trout", 120, "fish", "#B9C8C0");
    public static readonly FishingItemDef Pike = Fish("pike", "Pike", 250, "fish_long", "#6F9A52");
    public static readonly FishingItemDef Eel = Fish("eel", "Eel", 500, "eel", "#7A8A5A");
    public static readonly FishingItemDef Catfish = Fish("catfish", "Catfish", 1_000, "catfish", "#A08A70");
    public static readonly FishingItemDef GoldenCarp = Fish("goldencarp", "Golden Carp", 8_000, "fish", "#F2C230");

    // ---------------------------------------------------------------- fish of Driftwood Cove
    public static readonly FishingItemDef Mackerel = Fish("mackerel", "Mackerel", 80, "fish_long", "#6A9AD0");
    public static readonly FishingItemDef SeaBass = Fish("seabass", "Sea Bass", 300, "fish", "#A8B8C8");
    public static readonly FishingItemDef Snapper = Fish("snapper", "Snapper", 900, "fish", "#E8604E");
    public static readonly FishingItemDef Swordfish = Fish("swordfish", "Swordfish", 12_000, "swordfish", "#5A7AC0");

    // ---------------------------------------------------------------- lake drops
    public static readonly FishingItemDef LilyPad = new("lilypad", "Lily Pad", ItemKind.LakeDrop, 30, "lilypad", "#6CC24A");
    public static readonly FishingItemDef Clay = new("clay", "Clay", ItemKind.LakeDrop, 40, "clay", "#A8B0C0");
    public static readonly FishingItemDef InkSac = new("inksac", "Ink Sac", ItemKind.LakeDrop, 80, "inksac", "#8A8AC0");
    public static readonly FishingItemDef Sponge = new("sponge", "Sponge", ItemKind.LakeDrop, 250, "sponge", "#E8D04A");

    // ---------------------------------------------------------------- treasure: crafting materials
    public static readonly FishingItemDef WaxedCanvas = new("waxedcanvas", "Waxed Canvas", ItemKind.Treasure, 1_500,
        "canvas", "#E0C878", "Used to make fishing armour.");
    public static readonly FishingItemDef BrassHook = new("brasshook", "Brass Hook", ItemKind.Treasure, 2_000,
        "hook", "#E8B040", "Used to make rods.");
    public static readonly FishingItemDef SpoolOfLine = new("spool", "Spool of Line", ItemKind.Treasure, 2_500,
        "spool", "#E8E0D0", "Used to make rods and fishing armour.");
    public static readonly FishingItemDef RiverPearl = new("riverpearl", "River Pearl", ItemKind.Treasure, 15_000,
        "pearl", "#F4F0FF", "A great catch. Used for the best rods and armour.");
    public static readonly FishingItemDef TideGem = new("tidegem", "Tide Gem", ItemKind.Treasure, 40_000,
        "tidegem", "#4AD9C8", "A great catch. Used for the best rods and armour.");

    // ---------------------------------------------------------------- junk (Driftwood Cove only)
    public static readonly FishingItemDef TinCan = new("tincan", "Tin Can", ItemKind.Junk, 15, "tincan", "#B0B8C0",
        "Rusty Rowan takes junk in trade.");
    public static readonly FishingItemDef SoggyBoot = new("soggyboot", "Soggy Boot", ItemKind.Junk, 20, "soggyboot", "#A07A50",
        "Rusty Rowan takes junk in trade.");
    public static readonly FishingItemDef TangledNet = new("tanglednet", "Tangled Net", ItemKind.Junk, 25, "tanglednet", "#A0B07A",
        "Rusty Rowan takes junk in trade.");
    public static readonly FishingItemDef BrokenBottle = new("brokenbottle", "Broken Bottle", ItemKind.Junk, 20, "brokenbottle", "#6CC2A0",
        "Rusty Rowan takes junk in trade.");
    public static readonly FishingItemDef RustyAnchor = new("rustyanchor", "Rusty Anchor", ItemKind.Junk, 500, "rustyanchor", "#C0703A",
        "Rare junk. Rusty Rowan pays well for it.");

    // ---------------------------------------------------------------- bait
    public static readonly FishingItemDef WrigglerBait = new("wrigglerbait", "Wriggler Bait", ItemKind.Bait, 10, "bait", "#E07A8A",
        "+30 Fishing Speed. One is used up on every catch.");
    public static readonly FishingItemDef GlimmerBait = new("glimmerbait", "Glimmer Bait", ItemKind.Bait, 15, "bait", "#F2D36B",
        "+3% Treasure Chance. One is used up on every catch.");
    public static readonly FishingItemDef TwinBait = new("twinbait", "Twin Bait", ItemKind.Bait, 20, "bait", "#59C7EE",
        "+20% Double Catch chance. One is used up on every catch.");

    /// <summary>Everything, in the order the Inventory and Bazaar show them.</summary>
    public static readonly IReadOnlyList<FishingItemDef> All = new[]
    {
        Perch, Carp, Trout, Pike, Eel, Catfish, GoldenCarp,
        Mackerel, SeaBass, Snapper, Swordfish,
        LilyPad, Clay, InkSac, Sponge,
        WaxedCanvas, BrassHook, SpoolOfLine, RiverPearl, TideGem,
        TinCan, SoggyBoot, TangledNet, BrokenBottle, RustyAnchor,
        WrigglerBait, GlimmerBait, TwinBait,
    };

    public static FishingItemDef? Find(string? id) => All.FirstOrDefault(i => i.Id == id);
}

/// <summary>Bonuses to the fishing stats. Treasure and DoubleCatch are percentages.</summary>
public readonly record struct FishingBonus(int Speed = 0, double Treasure = 0, double DoubleCatch = 0)
{
    public bool IsEmpty => Speed == 0 && Treasure == 0 && DoubleCatch == 0;

    public static FishingBonus operator +(FishingBonus a, FishingBonus b) =>
        new(a.Speed + b.Speed, a.Treasure + b.Treasure, a.DoubleCatch + b.DoubleCatch);

    /// <summary>"+35 Fishing Speed, +0.5% Treasure Chance".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Speed != 0) parts.Add($"+{Speed} Fishing Speed");
        if (Treasure != 0) parts.Add($"+{Treasure:0.##}% Treasure Chance");
        if (DoubleCatch != 0) parts.Add($"+{DoubleCatch:0.##}% Double Catch");
        return parts.Count == 0 ? "No bonus" : string.Join(", ", parts);
    }
}

/// <summary>One thing that can bite on a lake.</summary>
/// <param name="Weight">How often it bites compared with the others (out of the total weight).</param>
/// <param name="FishingLevel">Fishing level needed before it starts biting.</param>
/// <param name="Xp">Fishing XP for catching one.</param>
public sealed record CatchEntry(string ItemId, int Weight, int FishingLevel, double Xp)
{
    public ItemDef Item => Items.Get(ItemId);
}

/// <summary>A treasure catch. <paramref name="Great"/> = a rare "Great catch!".</summary>
public sealed record TreasureEntry(string ItemId, int Weight, double Xp, bool Great)
{
    public ItemDef Item => Items.Get(ItemId);
}

/// <summary>A lake you can fish on, on your island or another one.</summary>
/// <param name="BoatTripCost">Coins for the boat trip that unlocks it (0 = it's on your island).</param>
public sealed record LakeDef(
    string Id,
    string Name,
    string Island,
    string Description,
    long BoatTripCost,
    IReadOnlyList<CatchEntry> Catches)
{
    public bool IsOnAnotherIsland => BoatTripCost > 0;
}

/// <summary>The places the farmer can be: the field, or a lake.</summary>
public static class Places
{
    public const string Field = "field";

    public static readonly LakeDef WillowLake = new(
        "willow", "Willow Lake", "your island",
        "A quiet lake next to your field. Fish, lily pads and clay.",
        0,
        new CatchEntry[]
        {
            new(FishingItems.Perch.Id, 40, 0, 12),
            new(FishingItems.Carp.Id, 35, 0, 14),
            new(FishingItems.LilyPad.Id, 15, 0, 6),
            new(FishingItems.Clay.Id, 12, 0, 6),
            new(FishingItems.Trout.Id, 25, 3, 25),
            new(FishingItems.InkSac.Id, 12, 5, 10),
            new(FishingItems.Pike.Id, 18, 6, 40),
            new(FishingItems.Eel.Id, 12, 10, 65),
            new(FishingItems.Sponge.Id, 6, 12, 20),
            new(FishingItems.Catfish.Id, 8, 15, 100),
            new(FishingItems.GoldenCarp.Id, 1, 20, 500),
        });

    public static readonly LakeDef DriftwoodCove = new(
        "cove", "Driftwood Cove", "Driftwood Cove",
        "A murky cove on another island, full of junk. Rusty Rowan trades for it.",
        50_000,
        new CatchEntry[]
        {
            new(FishingItems.Mackerel.Id, 40, 0, 20),
            new(FishingItems.TinCan.Id, 25, 0, 5),
            new(FishingItems.SoggyBoot.Id, 18, 0, 5),
            new(FishingItems.TangledNet.Id, 15, 0, 5),
            new(FishingItems.BrokenBottle.Id, 15, 0, 5),
            new(FishingItems.InkSac.Id, 15, 0, 10),
            new(FishingItems.SeaBass.Id, 25, 8, 50),
            new(FishingItems.Sponge.Id, 10, 8, 20),
            new(FishingItems.Snapper.Id, 15, 14, 90),
            new(FishingItems.RustyAnchor.Id, 3, 0, 50),
            new(FishingItems.Swordfish.Id, 2, 22, 600),
        });

    public static readonly IReadOnlyList<LakeDef> Lakes = new[] { WillowLake, DriftwoodCove };

    /// <summary>Treasure catches: the same on every lake. Great catches are rarer.</summary>
    public static readonly IReadOnlyList<TreasureEntry> Treasure = new TreasureEntry[]
    {
        new(FishingItems.WaxedCanvas.Id, 35, 80, false),
        new(FishingItems.BrassHook.Id, 30, 80, false),
        new(FishingItems.SpoolOfLine.Id, 25, 80, false),
        new(FishingItems.RiverPearl.Id, 7, 300, true),
        new(FishingItems.TideGem.Id, 3, 300, true),
    };

    public static LakeDef? FindLake(string? id) => Lakes.FirstOrDefault(l => l.Id == id);

    public static bool IsKnown(string? id) => id == Field || FindLake(id) is not null;

    public static string Name(string? id) => FindLake(id)?.Name ?? "The field";
}

/// <summary>
/// Fishing skill: earn Fishing XP from every catch. Uses the same XP curve as Farming.
/// Each level adds Fishing Speed.
/// </summary>
public static class FishingSkill
{
    public const int MaxLevel = FarmingSkill.MaxLevel;

    /// <summary>Fishing Speed gained per Fishing level.</summary>
    public const int SpeedPerLevel = 1;

    /// <summary>Coins rewarded when reaching a level = level * this.</summary>
    public const int CoinRewardPerLevel = 75;

    public static long XpToNextLevel(int level) => FarmingSkill.XpToNextLevel(level);
    public static long TotalXpFor(int level) => FarmingSkill.TotalXpFor(level);
    public static int LevelForXp(double xp) => FarmingSkill.LevelForXp(xp);
    public static double ProgressToNext(double xp) => FarmingSkill.ProgressToNext(xp);
}

public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

public static class Rarities
{
    public static string Name(Rarity rarity) => rarity.ToString().ToUpperInvariant();

    public static string ColorHex(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => "#55D455",
        Rarity.Rare => "#5A8AF0",
        Rarity.Epic => "#B566E8",
        Rarity.Legendary => "#F0A030",
        _ => "#E8E8E8",
    };
}

/// <summary>
/// Rods: everyone starts with a Fishing Rod, and each better rod needs a Fishing level, fish,
/// treasure and coins. (Sea Creature Chance, Damage and Strength come with sea creatures.)
/// </summary>
public static class Rods
{
    public const int MaxTier = 5;

    /// <summary>The recipe target for rods.</summary>
    public const string Target = "Rod";

    private static readonly (string Name, Core.Rarity Rarity, int Level, FishingBonus Bonus)[] Tiers =
    {
        ("No rod", Core.Rarity.Common, 0, new FishingBonus()),
        ("Fishing Rod", Core.Rarity.Common, 0, new FishingBonus(Speed: 5)),
        ("Angler's Rod", Core.Rarity.Uncommon, 10, new FishingBonus(Speed: 35, Treasure: 0.5)),
        ("Lakewarden Rod", Core.Rarity.Rare, 15, new FishingBonus(Speed: 50, Treasure: 1)),
        ("Stormcatcher Rod", Core.Rarity.Epic, 20, new FishingBonus(Speed: 65, Treasure: 1.5)),
        ("Tidecaller Rod", Core.Rarity.Legendary, 24, new FishingBonus(Speed: 80, Treasure: 2.5)),
    };

    private static int Clamp(int tier) => Math.Clamp(tier, 0, MaxTier);

    public static string Name(int tier) => Tiers[Clamp(tier)].Name;
    public static Core.Rarity Rarity(int tier) => Tiers[Clamp(tier)].Rarity;
    public static int FishingLevel(int tier) => Tiers[Clamp(tier)].Level;
    public static FishingBonus Bonus(int tier) => Tiers[Clamp(tier)].Bonus;
}

/// <summary>
/// Fishing armour: four pieces, each upgraded through three sets, like the farming armour.
/// It's worn while fishing (and the farming armour while farming), so it only gives fishing stats.
/// </summary>
public static class FishingArmour
{
    public const int MaxTier = 3;

    private static readonly string[] SetNames = { "", "Oilskin", "Mariner", "Deepwater" };

    private static readonly FishingBonus[] PerPiece =
    {
        new(),
        new(Speed: 2, Treasure: 0.25),
        new(Speed: 4, Treasure: 0.5),
        new(Speed: 8, Treasure: 1),
    };

    public static string SetName(int tier) => SetNames[Math.Clamp(tier, 0, MaxTier)];

    /// <summary>Hat, coat, waders or boots.</summary>
    public static string PieceName(ArmourSlot slot) => slot switch
    {
        ArmourSlot.Helmet => "Hat",
        ArmourSlot.Chestplate => "Coat",
        ArmourSlot.Leggings => "Waders",
        _ => "Boots",
    };

    public static string Name(ArmourSlot slot, int tier) =>
        tier <= 0 ? $"No fishing {PieceName(slot).ToLowerInvariant()}" : $"{SetName(tier)} {PieceName(slot)}";

    public static FishingBonus Bonus(int tier) => PerPiece[Math.Clamp(tier, 0, MaxTier)];

    /// <summary>The bonus for wearing a full set of this tier.</summary>
    public static FishingBonus SetBonus(int tier) => tier switch
    {
        1 => new FishingBonus(Speed: 5),
        2 => new FishingBonus(Speed: 10, Treasure: 0.5),
        3 => new FishingBonus(Speed: 15, Treasure: 1.5),
        _ => new FishingBonus(),
    };

    /// <summary>The piece's picture in sprites.txt (recoloured per set by the game).</summary>
    public static string Sprite(ArmourSlot slot) => slot switch
    {
        ArmourSlot.Helmet => "fishing_hat",
        ArmourSlot.Chestplate => "fishing_coat",
        ArmourSlot.Leggings => "fishing_waders",
        _ => "fishing_boots",
    };
}

/// <summary>What each bait does while it's chosen (one is used up on every catch).</summary>
public static class Baits
{
    public static readonly IReadOnlyDictionary<string, FishingBonus> Bonus = new Dictionary<string, FishingBonus>
    {
        [FishingItems.WrigglerBait.Id] = new(Speed: 30),
        [FishingItems.GlimmerBait.Id] = new(Treasure: 3),
        [FishingItems.TwinBait.Id] = new(DoubleCatch: 20),
    };

    public static IEnumerable<ItemDef> All => Bonus.Keys.Select(Items.Get);

    public static bool IsBait(string? id) => id is not null && Bonus.ContainsKey(id);
}

/// <summary>Where a trade happens: the bait table (anywhere) or Rusty Rowan's junk shop (Driftwood Cove).</summary>
public enum TradeSource { BaitTable, JunkShop }

/// <summary>
/// A trade you can make as often as you like: these items (and coins) for <see cref="ResultCount"/>
/// of <see cref="ResultItemId"/>, or for a cosmetic.
/// </summary>
public sealed record TradeDef(
    string Id,
    TradeSource Source,
    IReadOnlyList<Ingredient> Cost,
    long Coins,
    string? ResultItemId,
    int ResultCount,
    int FishingLevel = 0,
    string? Cosmetic = null)
{
    public ItemDef? Result => ResultItemId is null ? null : Items.Get(ResultItemId);

    /// <summary>A cosmetic can only be bought once.</summary>
    public bool IsOneOff => Cosmetic is not null;
}

/// <summary>Bait recipes and Rusty Rowan's junk shop.</summary>
public static class Trades
{
    public const string ShopkeeperName = "Rusty Rowan";

    private static Ingredient Of(FishingItemDef item, long count) => new(item.Id, count);

    public static readonly IReadOnlyList<TradeDef> All = new TradeDef[]
    {
        // ---------------------------------------------------------------- the bait table
        new("bait_wriggler", TradeSource.BaitTable,
            new[] { Of(FishingItems.Perch, 1), Of(FishingItems.LilyPad, 1) }, 0, FishingItems.WrigglerBait.Id, 25),
        new("bait_glimmer", TradeSource.BaitTable,
            new[] { Of(FishingItems.Carp, 2), Of(FishingItems.InkSac, 1) }, 0, FishingItems.GlimmerBait.Id, 25, FishingLevel: 5),
        new("bait_twin", TradeSource.BaitTable,
            new[] { Of(FishingItems.Pike, 2), Of(FishingItems.Clay, 2) }, 0, FishingItems.TwinBait.Id, 25, FishingLevel: 8),

        // ---------------------------------------------------------------- Rusty Rowan's junk shop
        new("junk_wriggler", TradeSource.JunkShop,
            new[] { Of(FishingItems.TinCan, 4) }, 0, FishingItems.WrigglerBait.Id, 50),
        new("junk_glimmer", TradeSource.JunkShop,
            new[] { Of(FishingItems.SoggyBoot, 3), Of(FishingItems.TinCan, 2) }, 0, FishingItems.GlimmerBait.Id, 50),
        new("junk_twin", TradeSource.JunkShop,
            new[] { Of(FishingItems.TangledNet, 3), Of(FishingItems.BrokenBottle, 2) }, 0, FishingItems.TwinBait.Id, 50),
        new("junk_canvas", TradeSource.JunkShop,
            new[] { Of(FishingItems.SoggyBoot, 6), Of(FishingItems.TangledNet, 4) }, 0, FishingItems.WaxedCanvas.Id, 1),
        new("junk_hook", TradeSource.JunkShop,
            new[] { Of(FishingItems.TinCan, 8), Of(FishingItems.BrokenBottle, 4) }, 0, FishingItems.BrassHook.Id, 1),
        new("junk_spool", TradeSource.JunkShop,
            new[] { Of(FishingItems.TangledNet, 8), Of(FishingItems.SoggyBoot, 4) }, 0, FishingItems.SpoolOfLine.Id, 1),
        new("junk_pearl", TradeSource.JunkShop,
            new[] { Of(FishingItems.RustyAnchor, 3), Of(FishingItems.BrokenBottle, 20) }, 0, FishingItems.RiverPearl.Id, 1),
        new("junk_tidegem", TradeSource.JunkShop,
            new[] { Of(FishingItems.RustyAnchor, 8), Of(FishingItems.TinCan, 30) }, 0, FishingItems.TideGem.Id, 1),
        new("junk_buckethat", TradeSource.JunkShop,
            new[] { Of(FishingItems.SoggyBoot, 25), Of(FishingItems.RustyAnchor, 2) }, 0, null, 0, Cosmetic: "buckethat"),
        new("junk_beachcomber", TradeSource.JunkShop,
            new[] { Of(FishingItems.TangledNet, 40), Of(FishingItems.RustyAnchor, 2) }, 0, null, 0, Cosmetic: "beachcomber"),
    };

    public static TradeDef? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

    public static IEnumerable<TradeDef> From(TradeSource source) => All.Where(t => t.Source == source);
}
