using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>A Random that always rolls the same number, so drop counts are predictable.</summary>
internal sealed class FixedRandom(double value) : Random
{
    public override double NextDouble() => value;
}

public class GameEngineTests
{
    private static GameEngine NewEngine(double roll = 0.99) =>
        new(GameState.CreateNew(), new FixedRandom(roll));

    private static void Ripen(GameEngine engine) => engine.Tick(10_000);

    [Fact]
    public void New_game_has_three_wheat_plots_and_nothing_else()
    {
        var engine = NewEngine();
        Assert.Equal(3, engine.Plots.Count);
        Assert.All(engine.Plots, p => Assert.Equal("wheat", p.CropId));
        Assert.Equal(0, engine.State.Coins);
        Assert.Equal(0, engine.FarmingLevel);
        Assert.Equal(1, engine.GardenLevel);
        Assert.Equal(new[] { Crops.Wheat }, engine.UnlockedCrops);
    }

    [Fact]
    public void Crops_grow_through_stages_and_ripen_on_time()
    {
        var engine = NewEngine();
        var plot = engine.Plots[0];
        Assert.Equal(0, GameEngine.GrowthStage(plot));

        engine.Tick(Crops.Wheat.GrowSeconds * 0.3);
        Assert.Equal(1, GameEngine.GrowthStage(plot));

        engine.Tick(Crops.Wheat.GrowSeconds * 0.4);
        Assert.Equal(2, GameEngine.GrowthStage(plot));
        Assert.False(GameEngine.IsRipe(plot));

        engine.Tick(Crops.Wheat.GrowSeconds);
        Assert.True(GameEngine.IsRipe(plot));
        Assert.Equal(3, GameEngine.GrowthStage(plot));
        Assert.Equal(Crops.Wheat.GrowSeconds, plot.Growth); // capped, doesn't keep counting
    }

    [Fact]
    public void Unripe_crops_cannot_be_harvested()
    {
        var engine = NewEngine();
        engine.Tick(5);
        Assert.Null(engine.Harvest(0, handHarvest: true));
        Assert.Equal(0, engine.State.FarmingXp);
    }

    [Fact]
    public void Harvest_gives_items_xp_and_replants()
    {
        var engine = NewEngine(roll: 0.99); // never wins the "extra drop" roll
        Ripen(engine);

        var result = engine.Harvest(0, handHarvest: false);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Drops); // 3 base drops, 0 fortune at level 0
        Assert.Equal(3, engine.GetInventory(Crops.Wheat));
        Assert.Equal(3, engine.GetCollection(Crops.Wheat));
        Assert.Equal(Crops.Wheat.FarmingXp, engine.State.FarmingXp);
        Assert.Equal(Crops.Wheat.GardenXp, engine.State.GardenXp);
        Assert.Equal(0, engine.Plots[0].Growth);
        Assert.Equal("wheat", engine.Plots[0].CropId);
        Assert.Equal(1, engine.State.Stats.TotalHarvests);
    }

    [Fact]
    public void Hand_harvest_adds_fortune_bonus()
    {
        var engine = NewEngine(roll: 0.0); // always wins the extra drop roll
        Assert.Equal(GameEngine.HandHarvestFortuneBonus, engine.GetFortune(Crops.Wheat, handHarvest: true));

        Ripen(engine);
        var result = engine.Harvest(0, handHarvest: true)!;

        // 3 * 1.25 = 3.75 -> 3 guaranteed + 1 from the lucky roll
        Assert.Equal(4, result.Drops);
        Assert.True(result.HandHarvest);
        Assert.Equal(1, engine.State.Stats.HandHarvests);
    }

    [Fact]
    public void Fortune_adds_farming_level_and_collection_tier()
    {
        var engine = NewEngine();
        engine.State.FarmingXp = FarmingSkill.TotalXpFor(10);
        engine.State.Collections["wheat"] = CollectionTiers.Thresholds(Crops.Wheat)[2]; // tier 3

        Assert.Equal(10, engine.FarmingLevel);
        Assert.Equal(3, engine.GetCollectionTier(Crops.Wheat));
        Assert.Equal(10 * 4 + 3 * 2, engine.GetFortune(Crops.Wheat, handHarvest: false));
        Assert.Equal(3 * 1.46, engine.ExpectedDrops(Crops.Wheat, handHarvest: false), precision: 6);
    }

    [Fact]
    public void Tending_speeds_up_growth_but_not_past_ripe()
    {
        var engine = NewEngine();
        Assert.True(engine.Tend(0));
        Assert.Equal(Crops.Wheat.GrowSeconds * GameEngine.TendFraction, engine.Plots[0].Growth, precision: 6);

        Ripen(engine);
        Assert.False(engine.Tend(0));
        Assert.False(engine.Tend(99));
    }

    [Fact]
    public void Locked_crops_cannot_be_planted()
    {
        var engine = NewEngine();
        Assert.False(engine.Plant(0, "carrot"));
        Assert.Equal("wheat", engine.Plots[0].CropId);

        engine.State.GardenXp = GardenProgression.TotalXpFor(2);
        Assert.True(engine.Plant(0, "carrot"));
        Assert.Equal("carrot", engine.Plots[0].CropId);
        Assert.Equal(0, engine.Plots[0].Growth);
        Assert.Equal("carrot", engine.State.LastPlantedCrop);
    }

    [Fact]
    public void Plant_all_only_changes_plots_with_other_crops()
    {
        var engine = NewEngine();
        engine.State.GardenXp = GardenProgression.TotalXpFor(3);
        engine.Plant(0, "potato");
        engine.Tick(10);

        Assert.Equal(2, engine.PlantAll("potato"));
        Assert.All(engine.Plots, p => Assert.Equal("potato", p.CropId));
        Assert.Equal(10, engine.Plots[0].Growth); // the one already growing potatoes kept its progress
    }

    [Fact]
    public void Unlocking_a_plot_needs_garden_level_and_coins()
    {
        var engine = NewEngine();
        var cost = GardenProgression.PlotUnlocks[0];

        engine.State.Coins = 1_000_000;
        Assert.False(engine.TryUnlockNextPlot(out var reason));
        Assert.Contains("Garden Level", reason);

        engine.State.GardenXp = GardenProgression.TotalXpFor(cost.GardenLevel);
        engine.State.Coins = cost.Coins - 1;
        Assert.False(engine.TryUnlockNextPlot(out reason));
        Assert.Contains("coins", reason);

        engine.State.Coins = cost.Coins;
        Assert.True(engine.TryUnlockNextPlot(out _));
        Assert.Equal(4, engine.Plots.Count);
        Assert.Equal(0, engine.State.Coins);
    }

    [Fact]
    public void Every_plot_can_eventually_be_unlocked()
    {
        var engine = NewEngine();
        engine.State.GardenXp = GardenProgression.TotalXpFor(GardenProgression.MaxLevel);
        engine.State.Coins = long.MaxValue / 2;

        while (engine.TryUnlockNextPlot(out _)) { }

        Assert.Equal(GardenProgression.MaxPlots, engine.Plots.Count);
        Assert.False(engine.CanUnlockNextPlot(out var cost, out var reason));
        Assert.Null(cost);
        Assert.Equal("All plots unlocked", reason);
    }

    [Fact]
    public void Selling_turns_items_into_coins()
    {
        var engine = NewEngine();
        engine.State.Inventory["wheat"] = 10;

        Assert.Equal(4 * Crops.Wheat.SellPrice, engine.Sell("wheat", 4));
        Assert.Equal(6, engine.GetInventory(Crops.Wheat));
        Assert.Equal(6 * Crops.Wheat.SellPrice, engine.Sell("wheat", 999));
        Assert.Equal(0, engine.GetInventory(Crops.Wheat));
        Assert.False(engine.State.Inventory.ContainsKey("wheat"));
        Assert.Equal(10 * Crops.Wheat.SellPrice, engine.State.Coins);
        Assert.Equal(0, engine.Sell("wheat", 1));
    }

    [Fact]
    public void Sell_all_sells_every_crop()
    {
        var engine = NewEngine();
        engine.State.Inventory["wheat"] = 10;
        engine.State.Inventory["pumpkin"] = 2;
        int changes = 0;
        engine.Changed += () => changes++;

        long coins = engine.SellAll();

        Assert.Equal(10 * Crops.Wheat.SellPrice + 2 * Crops.Pumpkin.SellPrice, coins);
        Assert.Equal(coins, engine.State.Coins);
        Assert.Empty(engine.State.Inventory);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Auto_sell_pays_coins_instead_of_filling_inventory()
    {
        var engine = NewEngine();
        engine.State.Settings.AutoSell = true;
        Ripen(engine);

        var result = engine.Harvest(0, handHarvest: false)!;

        Assert.Equal(result.Drops * Crops.Wheat.SellPrice, result.CoinsFromAutoSell);
        Assert.Equal(result.CoinsFromAutoSell, engine.State.Coins);
        Assert.Empty(engine.State.Inventory);
        Assert.Equal(result.Drops, engine.GetCollection(Crops.Wheat)); // still counts for collections
    }

    [Fact]
    public void Farming_level_up_raises_event_and_pays_coins()
    {
        var engine = NewEngine();
        engine.State.FarmingXp = FarmingSkill.TotalXpFor(1) - 1;
        var levelUps = new List<LevelUp>();
        engine.LeveledUp += levelUps.Add;

        Ripen(engine);
        engine.Harvest(0, handHarvest: false);

        var farming = Assert.Single(levelUps, l => l.Skill == SkillKind.Farming);
        Assert.Equal(0, farming.OldLevel);
        Assert.Equal(1, farming.NewLevel);
        Assert.Equal(FarmingSkill.CoinRewardPerLevel, engine.State.Coins);
    }

    [Fact]
    public void Garden_level_up_announces_unlocked_crops()
    {
        var engine = NewEngine();
        engine.State.GardenXp = GardenProgression.TotalXpFor(2) - 1;
        var levelUps = new List<LevelUp>();
        engine.LeveledUp += levelUps.Add;

        Ripen(engine);
        engine.Harvest(0, handHarvest: false);

        var garden = Assert.Single(levelUps, l => l.Skill == SkillKind.Garden);
        Assert.Equal(2, garden.NewLevel);
        Assert.Contains("Unlocked Carrot", garden.Rewards);
        Assert.Contains(garden.Rewards, r => r.StartsWith("New plot available"));
        Assert.True(engine.IsCropUnlocked(Crops.Carrot));
    }

    [Fact]
    public void Reaching_a_collection_tier_gives_garden_xp()
    {
        var engine = NewEngine(roll: 0.99);
        long firstTier = CollectionTiers.Thresholds(Crops.Wheat)[0];
        engine.State.Collections["wheat"] = firstTier - 1;
        var tiers = new List<CollectionTierUp>();
        engine.CollectionTierReached += tiers.Add;

        Ripen(engine);
        var result = engine.Harvest(0, handHarvest: false)!;

        Assert.Equal(1, Assert.Single(tiers).NewTier);
        Assert.Equal(Crops.Wheat.GardenXp + CollectionTiers.GardenXpPerTier, result.GardenXp);
        Assert.Equal(CollectionTiers.FortunePerTier, engine.GetFortune(Crops.Wheat, handHarvest: false));
    }

    [Fact]
    public void Offline_progress_harvests_at_reduced_speed()
    {
        var engine = NewEngine(roll: 0.99);
        // 3 plots of wheat, all freshly planted. In "farmer time" one cycle is grow + walk.
        double cycle = Crops.Wheat.GrowSeconds + GameEngine.OfflineWalkSeconds;
        var away = TimeSpan.FromSeconds(cycle * 10 / GameEngine.OfflineEfficiency);

        var report = engine.ApplyOfflineProgress(away);

        Assert.Equal(30, report.Harvests);
        Assert.Equal(90, report.Drops["wheat"]);
        Assert.Equal(90, engine.GetInventory(Crops.Wheat));
        Assert.Equal(30 * Crops.Wheat.FarmingXp, report.FarmingXp);
        Assert.Equal(engine.State.FarmingXp, report.FarmingXp);
        Assert.True(report.FarmingLevelAfter > report.FarmingLevelBefore);
        Assert.False(report.IsEmpty);
    }

    [Fact]
    public void Offline_progress_is_capped()
    {
        var engine = NewEngine();
        var report = engine.ApplyOfflineProgress(TimeSpan.FromDays(30));
        Assert.Equal(GameEngine.MaxOfflineTime, report.Counted);
        Assert.Equal(TimeSpan.FromDays(30), report.Away);
    }

    [Fact]
    public void Offline_progress_does_not_raise_events()
    {
        var engine = NewEngine();
        int events = 0;
        engine.Harvested += _ => events++;
        engine.LeveledUp += _ => events++;
        engine.CollectionTierReached += _ => events++;

        engine.ApplyOfflineProgress(TimeSpan.FromHours(2));

        Assert.Equal(0, events);
        Assert.True(engine.State.Stats.TotalHarvests > 0);
    }

    [Fact]
    public void Offline_without_auto_harvest_only_grows_crops()
    {
        var engine = NewEngine();
        engine.State.Settings.FarmerAutoHarvest = false;

        var report = engine.ApplyOfflineProgress(TimeSpan.FromHours(1));

        Assert.True(report.IsEmpty);
        Assert.All(engine.Plots, p => Assert.True(GameEngine.IsRipe(p)));
    }

    [Fact]
    public void Reset_wipes_progress_but_keeps_settings()
    {
        var engine = NewEngine();
        engine.State.Coins = 500;
        engine.State.FarmingXp = 5000;
        engine.State.GardenXp = 5000;
        engine.State.Inventory["wheat"] = 9;
        engine.State.Collections["wheat"] = 9;
        engine.State.Settings.StripMode = StripMode.OnHover;
        engine.State.Coins += 1_000_000;
        engine.TryUnlockNextPlot(out _);

        engine.ResetProgress();

        Assert.Equal(0, engine.State.Coins);
        Assert.Equal(0, engine.FarmingLevel);
        Assert.Equal(1, engine.GardenLevel);
        Assert.Empty(engine.State.Inventory);
        Assert.Empty(engine.State.Collections);
        Assert.Equal(3, engine.Plots.Count);
        Assert.Equal(StripMode.OnHover, engine.State.Settings.StripMode);
    }

    [Fact]
    public void Each_new_crop_earns_more_coins_per_minute()
    {
        double Rate(CropDef c) => c.BaseDrops * c.SellPrice * 60 / c.GrowSeconds;
        for (int i = 1; i < Crops.All.Count; i++)
        {
            Assert.True(Rate(Crops.All[i]) > Rate(Crops.All[i - 1]),
                $"{Crops.All[i].Name} should earn more than {Crops.All[i - 1].Name}");
            Assert.True(Crops.All[i].UnlockGardenLevel > Crops.All[i - 1].UnlockGardenLevel);
        }
        Assert.Equal(Crops.All.Count, Crops.All.Select(c => c.Id).Distinct().Count());
        Assert.True(Crops.All[^1].UnlockGardenLevel <= GardenProgression.MaxLevel);
    }
}
