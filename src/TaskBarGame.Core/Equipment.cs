namespace TaskBarGame.Core;

/// <summary>
/// Hoes: every crop has its own hoe, in three tiers. A hoe only helps with its own crop,
/// and the farmer automatically uses the right one for whatever is planted.
/// </summary>
public static class Tools
{
    public const int MaxTier = 3;

    private static readonly string[] TierNames = { "", "Basic", "Reinforced", "Masterwork" };
    private static readonly int[] TierFortune = { 0, 10, 30, 60 };

    /// <summary>Every this many crops broken with a crop's hoe adds +1 Harvest Fortune for that crop.</summary>
    public const int BreaksPerCounterFortune = 1_000;

    public const int MaxCounterFortune = 30;

    public static string TierName(int tier) => TierNames[Math.Clamp(tier, 0, MaxTier)];

    public static string Name(CropDef crop, int tier) =>
        tier <= 0 ? "No hoe" : $"{TierName(tier)} {crop.Name} Hoe";

    public static int Fortune(int tier) => TierFortune[Math.Clamp(tier, 0, MaxTier)];

    /// <summary>The hoe gets better the more of its crop it breaks.</summary>
    public static int CounterFortune(long breaks) =>
        (int)Math.Min(MaxCounterFortune, Math.Max(0, breaks) / BreaksPerCounterFortune);
}

public enum ArmourSlot { Helmet, Chestplate, Leggings, Boots }

/// <summary>
/// Armour: four pieces, each upgraded through three sets. Wearing all four pieces of a set
/// (or better) gives that set's bonus; only the best full set's bonus counts.
/// </summary>
public static class Armour
{
    public const int MaxTier = 3;
    public static readonly IReadOnlyList<ArmourSlot> Slots = Enum.GetValues<ArmourSlot>();

    private static readonly string[] SetNames = { "", "Straw", "Harvester", "Verdant" };
    private static readonly int[] FortunePerPiece = { 0, 4, 10, 20 };

    public static string SetName(int tier) => SetNames[Math.Clamp(tier, 0, MaxTier)];

    public static string Name(ArmourSlot slot, int tier) =>
        tier <= 0 ? $"No {slot.ToString().ToLowerInvariant()}" : $"{SetName(tier)} {slot}";

    public static int Fortune(int tier) => FortunePerPiece[Math.Clamp(tier, 0, MaxTier)];

    /// <summary>The bonus for wearing a full set of this tier.</summary>
    public static StatBonus SetBonus(int tier) => tier switch
    {
        1 => new StatBonus(Speed: 5),
        2 => new StatBonus(Regrow: 10),
        3 => new StatBonus(Fortune: 25, Xp: 10),
        _ => new StatBonus(),
    };
}

public enum TalismanFamily { Clover, Swiftstep, Sprout, Scholar }

/// <summary>
/// Talismans: four kinds, each upgraded Charm → Ring → Relic. You just need to own one;
/// only its best tier counts.
/// </summary>
public static class Talismans
{
    public const int MaxTier = 3;
    public static readonly IReadOnlyList<TalismanFamily> Families = Enum.GetValues<TalismanFamily>();

    private static readonly string[] TierNames = { "", "Charm", "Ring", "Relic" };
    private static readonly int[] TierAmount = { 0, 5, 10, 20 };

    public static string TierName(int tier) => TierNames[Math.Clamp(tier, 0, MaxTier)];

    public static string FamilyName(TalismanFamily family) => family switch
    {
        TalismanFamily.Scholar => "Scholar's",
        _ => family.ToString(),
    };

    public static string Name(TalismanFamily family, int tier) =>
        tier <= 0 ? $"No {FamilyName(family)} talisman" : $"{FamilyName(family)} {TierName(tier)}";

    public static StatBonus Bonus(TalismanFamily family, int tier)
    {
        int amount = TierAmount[Math.Clamp(tier, 0, MaxTier)];
        return family switch
        {
            TalismanFamily.Clover => new StatBonus(Fortune: amount),
            TalismanFamily.Swiftstep => new StatBonus(Speed: amount),
            TalismanFamily.Sprout => new StatBonus(Regrow: amount),
            TalismanFamily.Scholar => new StatBonus(Xp: amount),
            _ => new StatBonus(),
        };
    }

    /// <summary>What this kind of talisman does, e.g. "Harvest Fortune".</summary>
    public static string Effect(TalismanFamily family) => family switch
    {
        TalismanFamily.Clover => "Harvest Fortune",
        TalismanFamily.Swiftstep => "Farming Speed",
        TalismanFamily.Sprout => "Regrow Speed",
        TalismanFamily.Scholar => "Farming XP",
        _ => "",
    };
}

/// <summary>Bonuses to the four farming stats. Speed, Regrow and Xp are percentages.</summary>
public readonly record struct StatBonus(int Fortune = 0, int Speed = 0, int Regrow = 0, int Xp = 0)
{
    public bool IsEmpty => Fortune == 0 && Speed == 0 && Regrow == 0 && Xp == 0;

    public static StatBonus operator +(StatBonus a, StatBonus b) =>
        new(a.Fortune + b.Fortune, a.Speed + b.Speed, a.Regrow + b.Regrow, a.Xp + b.Xp);

    /// <summary>"+25 Harvest Fortune, +10% Farming XP".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Fortune != 0) parts.Add($"+{Fortune} Harvest Fortune");
        if (Speed != 0) parts.Add($"+{Speed}% Farming Speed");
        if (Regrow != 0) parts.Add($"+{Regrow}% Regrow Speed");
        if (Xp != 0) parts.Add($"+{Xp}% Farming XP");
        return parts.Count == 0 ? "No bonus" : string.Join(", ", parts);
    }
}

public enum UpgradeKind { Tool, Armour, Talisman }

public sealed record Ingredient(string ItemId, long Count)
{
    public ItemDef Item => Items.Get(ItemId);
}

/// <summary>
/// A crafting recipe that upgrades one piece of equipment to <see cref="Tier"/>.
/// You need the tier below it already (except for tier 1).
/// </summary>
/// <param name="Target">The crop id (tools), armour slot or talisman family this upgrades.</param>
public sealed record Recipe(
    UpgradeKind Kind,
    string Target,
    int Tier,
    IReadOnlyList<Ingredient> Ingredients,
    long Coins,
    int GardenLevel)
{
    public string Id => $"{Kind}:{Target}:{Tier}".ToLowerInvariant();

    /// <summary>The name of what this recipe makes.</summary>
    public string ResultName => Kind switch
    {
        UpgradeKind.Tool => Tools.Name(Crops.Get(Target), Tier),
        UpgradeKind.Armour => Armour.Name(Enum.Parse<ArmourSlot>(Target), Tier),
        _ => Talismans.Name(Enum.Parse<TalismanFamily>(Target), Tier),
    };
}

/// <summary>Every crafting recipe in the game.</summary>
public static class Recipes
{
    public static readonly IReadOnlyList<Recipe> All = Build();

    private static readonly Dictionary<string, Recipe> ById = All.ToDictionary(r => r.Id);

    public static Recipe? Find(UpgradeKind kind, string target, int tier) =>
        ById.GetValueOrDefault($"{kind}:{target}:{tier}".ToLowerInvariant());

    private static Ingredient Raw(CropDef crop, long count) => new(Items.Raw(crop).Id, count);
    private static Ingredient Packed(CropDef crop, long count) => new(Items.Packed(crop).Id, count);
    private static Ingredient Dense(CropDef crop, long count) => new(Items.Dense(crop).Id, count);

    private static List<Recipe> Build()
    {
        var list = new List<Recipe>();

        // ------------------------------------------------ hoes: one per crop, made from that crop
        foreach (var crop in Crops.All)
        {
            list.Add(new Recipe(UpgradeKind.Tool, crop.Id, 1, new[] { Raw(crop, 48) }, 100,
                crop.UnlockGardenLevel));
            list.Add(new Recipe(UpgradeKind.Tool, crop.Id, 2, new[] { Packed(crop, 8) }, 5_000,
                Math.Max(crop.UnlockGardenLevel, 4)));
            list.Add(new Recipe(UpgradeKind.Tool, crop.Id, 3, new[] { Dense(crop, 1) }, 100_000,
                Math.Max(crop.UnlockGardenLevel, 10)));
        }

        // ------------------------------------------------ armour: Straw → Harvester → Verdant
        void Piece(ArmourSlot slot, int tier, Ingredient ingredient, long coins, int gardenLevel) =>
            list.Add(new Recipe(UpgradeKind.Armour, slot.ToString(), tier, new[] { ingredient }, coins, gardenLevel));

        Piece(ArmourSlot.Helmet, 1, Packed(Crops.Wheat, 2), 500, 2);
        Piece(ArmourSlot.Chestplate, 1, Packed(Crops.Wheat, 4), 500, 2);
        Piece(ArmourSlot.Leggings, 1, Packed(Crops.Wheat, 3), 500, 2);
        Piece(ArmourSlot.Boots, 1, Packed(Crops.Wheat, 2), 500, 2);

        Piece(ArmourSlot.Helmet, 2, Packed(Crops.Pumpkin, 6), 15_000, 6);
        Piece(ArmourSlot.Chestplate, 2, Packed(Crops.Melon, 12), 15_000, 6);
        Piece(ArmourSlot.Leggings, 2, Packed(Crops.SugarCane, 10), 15_000, 6);
        Piece(ArmourSlot.Boots, 2, Packed(Crops.Potato, 6), 15_000, 6);

        Piece(ArmourSlot.Helmet, 3, Dense(Crops.Cocoa, 2), 150_000, 12);
        Piece(ArmourSlot.Chestplate, 3, Dense(Crops.Cactus, 2), 150_000, 12);
        Piece(ArmourSlot.Leggings, 3, Dense(Crops.Mushroom, 1), 150_000, 12);
        Piece(ArmourSlot.Boots, 3, Dense(Crops.Moonbloom, 1), 150_000, 15);

        // ------------------------------------------------ talismans: Charm → Ring → Relic
        void Talisman(TalismanFamily family, int tier, Ingredient ingredient, long coins, int gardenLevel) =>
            list.Add(new Recipe(UpgradeKind.Talisman, family.ToString(), tier, new[] { ingredient }, coins, gardenLevel));

        Talisman(TalismanFamily.Clover, 1, Packed(Crops.Carrot, 4), 2_000, 3);
        Talisman(TalismanFamily.Clover, 2, Packed(Crops.Carrot, 24), 25_000, 7);
        Talisman(TalismanFamily.Clover, 3, Dense(Crops.Carrot, 2), 200_000, 12);

        Talisman(TalismanFamily.Swiftstep, 1, Packed(Crops.SugarCane, 4), 2_000, 4);
        Talisman(TalismanFamily.Swiftstep, 2, Packed(Crops.SugarCane, 24), 25_000, 8);
        Talisman(TalismanFamily.Swiftstep, 3, Dense(Crops.SugarCane, 2), 200_000, 13);

        Talisman(TalismanFamily.Sprout, 1, Packed(Crops.Potato, 4), 2_000, 3);
        Talisman(TalismanFamily.Sprout, 2, Packed(Crops.Melon, 24), 25_000, 8);
        Talisman(TalismanFamily.Sprout, 3, Dense(Crops.Melon, 2), 200_000, 13);

        Talisman(TalismanFamily.Scholar, 1, Packed(Crops.Wheat, 4), 2_000, 2);
        Talisman(TalismanFamily.Scholar, 2, Packed(Crops.Pumpkin, 24), 25_000, 7);
        Talisman(TalismanFamily.Scholar, 3, Dense(Crops.Cocoa, 2), 200_000, 12);

        return list;
    }
}
