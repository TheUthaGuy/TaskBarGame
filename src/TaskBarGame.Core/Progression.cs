namespace TaskBarGame.Core;

/// <summary>
/// Farming skill: earn XP from every harvest. Each level adds Harvest Fortune.
/// </summary>
public static class FarmingSkill
{
    public const int MaxLevel = 50;

    /// <summary>Harvest Fortune gained per Farming level.</summary>
    public const int FortunePerLevel = 4;

    /// <summary>Coins rewarded when reaching a level = level * this.</summary>
    public const int CoinRewardPerLevel = 50;

    private static readonly long[] TotalXpForLevel = BuildTable();

    private static long[] BuildTable()
    {
        // TotalXpForLevel[n] = XP needed in total to reach level n.
        var table = new long[MaxLevel + 1];
        for (int level = 1; level <= MaxLevel; level++)
            table[level] = table[level - 1] + XpToNextLevel(level - 1);
        return table;
    }

    /// <summary>XP needed to go from <paramref name="level"/> to level + 1.</summary>
    public static long XpToNextLevel(int level) => 100 + 50L * level * level;

    public static long TotalXpFor(int level) => TotalXpForLevel[Math.Clamp(level, 0, MaxLevel)];

    public static int LevelForXp(double xp)
    {
        int level = 0;
        while (level < MaxLevel && xp >= TotalXpForLevel[level + 1])
            level++;
        return level;
    }

    /// <summary>Progress towards the next level, 0..1 (1 at max level).</summary>
    public static double ProgressToNext(double xp)
    {
        int level = LevelForXp(xp);
        if (level >= MaxLevel) return 1;
        long start = TotalXpForLevel[level];
        return (xp - start) / (TotalXpForLevel[level + 1] - start);
    }
}

/// <summary>
/// Garden level: earn Garden XP from every crop you break and from collection milestones.
/// Garden levels unlock new crops, upgrades and field expansions.
/// </summary>
public static class GardenProgression
{
    public const int MaxLevel = 15;

    /// <summary>Garden XP for breaking one crop.</summary>
    public const int XpPerBreak = 1;

    /// <summary>Total Garden XP needed to reach each level. Index = level.</summary>
    private static readonly long[] TotalXpForLevel =
    {
        0,        // (unused, level 0)
        0,        // level 1
        400,      // level 2
        1_800,
        5_000,
        11_000,    // level 5
        20_000,
        34_000,
        54_000,
        82_000,
        120_000,   // level 10
        170_000,
        234_000,
        314_000,
        414_000,
        540_000,  // level 15
    };

    public static long TotalXpFor(int level) => TotalXpForLevel[Math.Clamp(level, 1, MaxLevel)];

    public static int LevelForXp(long xp)
    {
        int level = 1;
        while (level < MaxLevel && xp >= TotalXpForLevel[level + 1])
            level++;
        return level;
    }

    public static double ProgressToNext(long xp)
    {
        int level = LevelForXp(xp);
        if (level >= MaxLevel) return 1;
        long start = TotalXpForLevel[level];
        return (double)(xp - start) / (TotalXpForLevel[level + 1] - start);
    }
}

/// <summary>The field is one long row of crops. Expansions make the row longer.</summary>
public static class FieldProgression
{
    /// <summary>Crops in the row when you start.</summary>
    public const int StartLength = 10;

    /// <summary>Each expansion, in order: the new length, its cost and the Garden level needed.</summary>
    public static readonly IReadOnlyList<FieldExpansion> Expansions = new FieldExpansion[]
    {
        new(Length: 12, Coins: 500,     GardenLevel: 2),
        new(Length: 14, Coins: 2_500,   GardenLevel: 4),
        new(Length: 16, Coins: 10_000,  GardenLevel: 6),
        new(Length: 18, Coins: 40_000,  GardenLevel: 8),
        new(Length: 20, Coins: 120_000, GardenLevel: 11),
        new(Length: 24, Coins: 400_000, GardenLevel: 14),
    };

    public static int MaxLength => Expansions[^1].Length;

    /// <summary>The next expansion for a row this long, or null when it's as long as it gets.</summary>
    public static FieldExpansion? Next(int length) => Expansions.FirstOrDefault(e => e.Length > length);
}

public sealed record FieldExpansion(int Length, long Coins, int GardenLevel);

/// <summary>
/// Collections: every crop item you harvest counts towards that crop's collection.
/// Each milestone tier gives Harvest Fortune for that crop plus Garden XP.
/// </summary>
public static class CollectionTiers
{
    public const int MaxTier = 10;
    public const int FortunePerTier = 2;

    /// <summary>Garden XP for reaching a tier = tier * this.</summary>
    public const int GardenXpPerTier = 50;

    /// <summary>Thresholds for Wheat. Other crops are scaled by how many items they drop.</summary>
    private static readonly long[] BaseThresholds =
        { 200, 1_000, 4_000, 12_000, 30_000, 80_000, 200_000, 500_000, 1_000_000, 2_000_000 };

    private static readonly Dictionary<string, long[]> ThresholdsByCrop =
        Crops.All.ToDictionary(c => c.Id, BuildThresholds);

    private static long[] BuildThresholds(CropDef crop)
    {
        // The farmer breaks crops at the same rate whatever is planted, so a crop that drops
        // 3 items needs 3 times as many for each tier; every collection takes about as long.
        double scale = (double)crop.DropsPerBreak / Crops.Wheat.DropsPerBreak;
        return BaseThresholds.Select(t => Math.Max(10, RoundNice(t * scale))).ToArray();
    }

    /// <summary>Rounds to 2 significant figures so numbers look tidy (e.g. 1,234 → 1,200).</summary>
    private static long RoundNice(double value)
    {
        if (value < 100) return (long)Math.Round(value);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
        return (long)(Math.Round(value / magnitude) * magnitude);
    }

    public static IReadOnlyList<long> Thresholds(CropDef crop) => ThresholdsByCrop[crop.Id];

    /// <summary>How many of a crop you need to collect to max out its collection (the last tier).</summary>
    public static long MaxThreshold(CropDef crop) => ThresholdsByCrop[crop.Id][MaxTier - 1];

    public static int TierFor(CropDef crop, long collected)
    {
        var thresholds = ThresholdsByCrop[crop.Id];
        int tier = 0;
        while (tier < thresholds.Length && collected >= thresholds[tier])
            tier++;
        return tier;
    }

    /// <summary>Items needed for the next tier, or null at max tier.</summary>
    public static long? NextThreshold(CropDef crop, long collected)
    {
        int tier = TierFor(crop, collected);
        return tier < MaxTier ? ThresholdsByCrop[crop.Id][tier] : null;
    }
}
