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
        Assert.Equal(1, GardenProgression.LevelForXp(199));
        Assert.Equal(2, GardenProgression.LevelForXp(200));
        Assert.Equal(GardenProgression.MaxLevel, GardenProgression.LevelForXp(long.MaxValue));
        for (int level = 2; level <= GardenProgression.MaxLevel; level++)
            Assert.True(GardenProgression.TotalXpFor(level) > GardenProgression.TotalXpFor(level - 1));
    }

    [Fact]
    public void Field_expansions_get_longer_and_more_expensive()
    {
        var expansions = FieldProgression.Expansions;
        Assert.True(expansions[0].Length > FieldProgression.StartLength);
        for (int i = 1; i < expansions.Count; i++)
        {
            Assert.True(expansions[i].Length > expansions[i - 1].Length);
            Assert.True(expansions[i].Coins > expansions[i - 1].Coins);
            Assert.True(expansions[i].GardenLevel >= expansions[i - 1].GardenLevel);
        }
        Assert.All(expansions, e => Assert.InRange(e.GardenLevel, 1, GardenProgression.MaxLevel));
        Assert.Equal(expansions[0], FieldProgression.Next(FieldProgression.StartLength));
        Assert.Null(FieldProgression.Next(FieldProgression.MaxLength));
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
