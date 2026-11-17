using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class ProgressionTests
{
    [Fact]
    public void Farming_levels_follow_the_xp_table()
    {
        Assert.Equal(0, FarmingSkill.LevelForXp(0));
        Assert.Equal(0, FarmingSkill.LevelForXp(49));
        Assert.Equal(1, FarmingSkill.LevelForXp(50));
        Assert.Equal(2, FarmingSkill.LevelForXp(125));
        Assert.Equal(FarmingSkill.MaxLevel, FarmingSkill.LevelForXp(long.MaxValue));
        Assert.Equal(0.5, FarmingSkill.ProgressToNext(25), precision: 6);
        Assert.Equal(1, FarmingSkill.ProgressToNext(FarmingSkill.TotalXpFor(FarmingSkill.MaxLevel)));
    }

    [Fact]
    public void Each_farming_level_costs_more_than_the_last()
    {
        for (int level = 1; level < FarmingSkill.MaxLevel; level++)
            Assert.True(FarmingSkill.XpToNextLevel(level) > FarmingSkill.XpToNextLevel(level - 1));
    }

    [Fact]
    public void Garden_levels_follow_the_xp_table()
    {
        Assert.Equal(1, GardenProgression.LevelForXp(0));
        Assert.Equal(1, GardenProgression.LevelForXp(59));
        Assert.Equal(2, GardenProgression.LevelForXp(60));
        Assert.Equal(GardenProgression.MaxLevel, GardenProgression.LevelForXp(long.MaxValue));
        for (int level = 2; level <= GardenProgression.MaxLevel; level++)
            Assert.True(GardenProgression.TotalXpFor(level) > GardenProgression.TotalXpFor(level - 1));
    }

    [Fact]
    public void Plot_unlocks_get_more_expensive()
    {
        Assert.Equal(12, GardenProgression.MaxPlots);
        var unlocks = GardenProgression.PlotUnlocks;
        for (int i = 1; i < unlocks.Count; i++)
        {
            Assert.True(unlocks[i].Coins > unlocks[i - 1].Coins);
            Assert.True(unlocks[i].GardenLevel >= unlocks[i - 1].GardenLevel);
        }
        Assert.All(unlocks, u => Assert.InRange(u.GardenLevel, 1, GardenProgression.MaxLevel));
        Assert.Equal(unlocks[0], GardenProgression.NextPlotUnlock(3));
        Assert.Null(GardenProgression.NextPlotUnlock(GardenProgression.MaxPlots));
    }

    [Fact]
    public void Collection_thresholds_increase_for_every_crop()
    {
        foreach (var crop in Crops.All)
        {
            var thresholds = CollectionTiers.Thresholds(crop);
            Assert.Equal(CollectionTiers.MaxTier, thresholds.Count);
            for (int i = 1; i < thresholds.Count; i++)
                Assert.True(thresholds[i] > thresholds[i - 1], $"{crop.Name} tier {i + 1}");
        }
    }

    [Fact]
    public void Collection_tiers_are_counted_from_thresholds()
    {
        var t = CollectionTiers.Thresholds(Crops.Wheat);
        Assert.Equal(0, CollectionTiers.TierFor(Crops.Wheat, 0));
        Assert.Equal(1, CollectionTiers.TierFor(Crops.Wheat, t[0]));
        Assert.Equal(CollectionTiers.MaxTier, CollectionTiers.TierFor(Crops.Wheat, long.MaxValue));
        Assert.Equal(t[1], CollectionTiers.NextThreshold(Crops.Wheat, t[0]));
        Assert.Null(CollectionTiers.NextThreshold(Crops.Wheat, long.MaxValue));
    }
}
