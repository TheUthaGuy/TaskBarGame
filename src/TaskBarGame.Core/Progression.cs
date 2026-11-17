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
    public static long XpToNextLevel(int level) => 50 + 25L * level * level;

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
/// Garden level: earn Garden XP from harvests and collection milestones.
/// Garden levels unlock new crops and let you buy more plots.
/// </summary>
public static class GardenProgression
{
    public const int MaxLevel = 15;

    /// <summary>Plots you start with.</summary>
    public const int StartingPlots = 3;

    /// <summary>Total Garden XP needed to reach each level. Index = level.</summary>
    private static readonly long[] TotalXpForLevel =
    {
        0,      // (unused, level 0)
        0,      // level 1
        60,     // level 2
        180,
        400,
        750,    // level 5
        1_250,
        2_000,
        3_200,
        5_000,
        7_500,  // level 10
        11_000,
        15_500,
        21_000,
        28_000,
        36_000, // level 15
    };

    /// <summary>Cost to unlock each extra plot, in order (plot 4, plot 5, ...).</summary>
    public static readonly IReadOnlyList<PlotUnlock> PlotUnlocks = new PlotUnlock[]
    {
        new(Coins: 200,    GardenLevel: 2),
        new(Coins: 500,    GardenLevel: 3),
        new(Coins: 1_000,  GardenLevel: 4),
        new(Coins: 2_000,  GardenLevel: 5),
        new(Coins: 4_000,  GardenLevel: 7),
        new(Coins: 8_000,  GardenLevel: 9),
        new(Coins: 15_000, GardenLevel: 11),
        new(Coins: 30_000, GardenLevel: 13),
        new(Coins: 60_000, GardenLevel: 15),
    };

    public static int MaxPlots => StartingPlots + PlotUnlocks.Count;

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

    /// <summary>The cost of the next plot, or null if every plot is unlocked.</summary>
    public static PlotUnlock? NextPlotUnlock(int unlockedPlots)
    {
        int index = unlockedPlots - StartingPlots;
        return index >= 0 && index < PlotUnlocks.Count ? PlotUnlocks[index] : null;
    }
}

public sealed record PlotUnlock(long Coins, int GardenLevel);

/// <summary>
/// Collections: every crop item you harvest counts towards that crop's collection.
/// Each milestone tier gives Harvest Fortune for that crop plus Garden XP.
/// </summary>
public static class CollectionTiers
{
    public const int MaxTier = 10;
    public const int FortunePerTier = 2;
    public const int GardenXpPerTier = 20;

    /// <summary>Thresholds for a crop that drops about 6 items per minute (Wheat).</summary>
    private static readonly long[] BaseThresholds =
        { 50, 150, 400, 1_000, 2_500, 6_000, 12_000, 25_000, 50_000, 100_000 };

    private static readonly Dictionary<string, long[]> ThresholdsByCrop =
        Crops.All.ToDictionary(c => c.Id, BuildThresholds);

    private static long[] BuildThresholds(CropDef crop)
    {
        // Slow crops drop fewer items, so their milestones are scaled down to take
        // about the same amount of time as Wheat's.
        double dropsPerMinute = crop.BaseDrops * 60.0 / crop.GrowSeconds;
        double scale = dropsPerMinute / 6.0;
        return BaseThresholds.Select(t => Math.Max(5, RoundNice(t * scale))).ToArray();
    }

    /// <summary>Rounds to 2 significant figures so numbers look tidy (e.g. 1,234 → 1,200).</summary>
    private static long RoundNice(double value)
    {
        if (value < 100) return (long)Math.Round(value);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)) - 1);
        return (long)(Math.Round(value / magnitude) * magnitude);
    }

    public static IReadOnlyList<long> Thresholds(CropDef crop) => ThresholdsByCrop[crop.Id];

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
