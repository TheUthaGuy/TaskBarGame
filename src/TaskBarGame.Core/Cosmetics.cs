namespace TaskBarGame.Core;

public enum CosmeticSlot { Hat, Outfit }

/// <summary>
/// A hat or outfit for the farmer. It unlocks when you reach its milestone, measured as
/// "have / need" (for example Farming Level 7 / 10) so the Wardrobe can show how close you are.
/// </summary>
public sealed record Cosmetic(
    string Id,
    CosmeticSlot Slot,
    string Name,
    string Milestone,
    Func<GameEngine, (long Have, long Need)> Progress)
{
    public bool IsUnlocked(GameEngine engine)
    {
        var (have, need) = Progress(engine);
        return have >= need;
    }
}

/// <summary>Every hat and outfit, and what unlocks it. The first of each is what the farmer starts with.</summary>
public static class Cosmetics
{
    public const string DefaultHat = "straw";
    public const string DefaultOutfit = "classic";

    public static readonly IReadOnlyList<Cosmetic> Hats = new Cosmetic[]
    {
        new("straw", CosmeticSlot.Hat, "Straw Hat", "Every farmer starts with one", _ => (1, 1)),
        new("cap", CosmeticSlot.Hat, "Farmer's Cap", "Reach Farming Level 10", e => (e.FarmingLevel, 10)),
        new("bandana", CosmeticSlot.Hat, "Bandana", "Break 10,000 crops", e => (e.State.Stats.TotalBreaks, 10_000)),
        new("tophat", CosmeticSlot.Hat, "Top Hat", "Earn 1,000,000 coins from selling", e => (e.State.Stats.CoinsEarned, 1_000_000)),
        new("flowers", CosmeticSlot.Hat, "Flower Crown", "Max out any collection (Tier X)",
            e => (Crops.All.Max(e.GetCollectionTier), CollectionTiers.MaxTier)),
        new("crown", CosmeticSlot.Hat, "Golden Crown", "Reach Farming Level 50", e => (e.FarmingLevel, FarmingSkill.MaxLevel)),
        Bought("hood", CosmeticSlot.Hat, "Traveller's Hood"),
        Bought("starhat", CosmeticSlot.Hat, "Stargazer's Hat"),
        new("souwester", CosmeticSlot.Hat, "Sou'wester", "Reach Fishing Level 10", e => (e.FishingLevel, 10)),
        Traded("buckethat", CosmeticSlot.Hat, "Bucket Hat"),
    };

    public static readonly IReadOnlyList<Cosmetic> Outfits = new Cosmetic[]
    {
        new("classic", CosmeticSlot.Outfit, "Classic Overalls", "Every farmer starts with these", _ => (1, 1)),
        new("harvest", CosmeticSlot.Outfit, "Harvest Brown", "Reach Garden Level 5", e => (e.GardenLevel, 5)),
        new("meadow", CosmeticSlot.Outfit, "Meadow Green", "Break 1,000 crops by hand", e => (e.State.Stats.HandBreaks, 1_000)),
        new("twilight", CosmeticSlot.Outfit, "Twilight Purple", "Reach Garden Level 10", e => (e.GardenLevel, 10)),
        new("golden", CosmeticSlot.Outfit, "Golden Harvest", "Wear a full Verdant armour set", e => (e.FullSetTier, Armour.MaxTier)),
        Bought("wanderer", CosmeticSlot.Outfit, "Wanderer's Garb"),
        Traded("beachcomber", CosmeticSlot.Outfit, "Beachcomber Blue"),
    };

    /// <summary>A cosmetic sold in the Visitor Shop for Garden Tokens.</summary>
    private static Cosmetic Bought(string id, CosmeticSlot slot, string name)
    {
        long price = Garden.Shop.First(i => i.Target == id).Cost;
        return new Cosmetic(id, slot, name, $"Buy it in the Visitor Shop ({price} tokens)",
            e => (e.State.Garden.OwnedCosmetics.Contains(id) ? 1 : 0, 1));
    }

    /// <summary>A cosmetic Rusty Rowan trades for junk at Driftwood Cove.</summary>
    private static Cosmetic Traded(string id, CosmeticSlot slot, string name) =>
        new(id, slot, name, $"Trade junk for it with {Trades.ShopkeeperName} at Driftwood Cove",
            e => (e.OwnsFishingCosmetic(id) ? 1 : 0, 1));

    public static IEnumerable<Cosmetic> All => Hats.Concat(Outfits);

    public static Cosmetic? Find(string? id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>The hat the farmer wears: the chosen one if it's unlocked, else the straw hat.</summary>
    public static Cosmetic WornHat(GameEngine engine) => Worn(engine, Hats, engine.State.Settings.Hat);

    public static Cosmetic WornOutfit(GameEngine engine) => Worn(engine, Outfits, engine.State.Settings.Outfit);

    private static Cosmetic Worn(GameEngine engine, IReadOnlyList<Cosmetic> list, string? chosen) =>
        list.FirstOrDefault(c => c.Id == chosen && c.IsUnlocked(engine)) ?? list[0];

    /// <summary>
    /// Cosmetics that are unlocked now but weren't announced yet; marks them announced.
    /// (The ones everyone starts with are never announced.)
    /// </summary>
    public static IReadOnlyList<Cosmetic> TakeNewlyUnlocked(GameEngine engine)
    {
        var announced = engine.State.UnlockedCosmetics;
        var fresh = All
            .Where(c => c != Hats[0] && c != Outfits[0] && !announced.Contains(c.Id) && c.IsUnlocked(engine))
            .ToList();
        foreach (var cosmetic in fresh) announced.Add(cosmetic.Id);
        return fresh;
    }
}
