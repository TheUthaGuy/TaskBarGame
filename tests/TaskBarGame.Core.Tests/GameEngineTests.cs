using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>A Random that always rolls the same number, so drop counts are predictable.</summary>
internal sealed class FixedRandom(double value) : Random
{
    public override double NextDouble() => value;
}

public class GameEngineTests
{
    private static GameEngine NewEngine(double roll = 0.99) => new(GameState.CreateNew(), new FixedRandom(roll));

    private static GameEngine RichEngine(int gardenLevel = GardenProgression.MaxLevel)
    {
        var engine = NewEngine();
        engine.State.GardenXp = GardenProgression.TotalXpFor(gardenLevel);
        engine.State.Coins = 10_000_000;
        return engine;
    }

    private static void GrowAll(GameEngine engine) => engine.Tick(1_000);

    private static void Give(GameEngine engine, ItemDef item, long count) =>
        engine.State.Inventory[item.Id] = engine.GetItem(item) + count;

    // ------------------------------------------------------------------ the field

    [Fact]
    public void New_game_has_a_row_of_wheat_and_no_equipment()
    {
        var engine = NewEngine();
        Assert.Equal(Crops.Wheat, engine.FieldCrop);
        Assert.Equal(FieldProgression.StartLength, engine.FieldLength);
        Assert.Equal(FieldProgression.StartLength, engine.Tiles.Count);
        Assert.All(engine.Tiles, t => Assert.Equal(0, t));
        Assert.Equal(0, engine.ActiveTool.Tier);
        Assert.Equal(0, engine.FullSetTier);
        Assert.True(engine.EquipmentBonus.IsEmpty);
        Assert.Equal(1, engine.GardenLevel);
        Assert.Equal(0, engine.FarmingLevel);
    }

    [Fact]
    public void Crops_regrow_through_stages()
    {
        var engine = NewEngine();
        double regrow = Crops.Wheat.RegrowSeconds;
        Assert.Equal(0, engine.GrowthStage(0));

        engine.Tick(regrow * 0.3);
        Assert.Equal(1, engine.GrowthStage(0));
        engine.Tick(regrow * 0.4);
        Assert.Equal(2, engine.GrowthStage(0));
        Assert.False(engine.IsGrown(0));

        engine.Tick(regrow);
        Assert.True(engine.IsGrown(0));
        Assert.Equal(3, engine.GrowthStage(0));
        Assert.Equal(regrow, engine.Tiles[0]);
        Assert.Equal(engine.FieldLength, engine.GrownCount);
    }

    [Fact]
    public void Only_grown_crops_break()
    {
        var engine = NewEngine();
        engine.Tick(1);
        Assert.Null(engine.Break(0, handBreak: true));
        Assert.Equal(0, engine.State.Stats.TotalBreaks);
    }

    [Fact]
    public void Breaking_gives_items_and_xp_and_regrows()
    {
        var engine = NewEngine(roll: 0.99);
        GrowAll(engine);

        var result = engine.Break(3, handBreak: false)!;

        Assert.Equal(3, result.TileIndex);
        Assert.Equal(Crops.Wheat.DropsPerBreak, result.Drops);
        Assert.Equal(result.Drops, engine.GetItem(Items.Raw(Crops.Wheat)));
        Assert.Equal(result.Drops, engine.GetCollection(Crops.Wheat));
        Assert.Equal(Crops.Wheat.FarmingXp, engine.State.FarmingXp);
        Assert.Equal(GardenProgression.XpPerBreak, engine.State.GardenXp);
        Assert.Equal(0, engine.Tiles[3]);
        Assert.False(engine.IsGrown(3));
        Assert.True(engine.IsGrown(4));
    }

    [Fact]
    public void Breaking_by_hand_adds_fortune()
    {
        var engine = NewEngine(roll: 0.0); // always wins the extra-drop roll
        engine.PlantField("wheat");
        GrowAll(engine);

        var result = engine.Break(0, handBreak: true)!;

        Assert.Equal(2, result.Drops); // 1 * 1.25 = 1.25 → 1 + lucky roll
        Assert.Equal(1, engine.State.Stats.HandBreaks);
    }

    [Fact]
    public void Tending_speeds_up_a_growing_crop()
    {
        var engine = NewEngine();
        Assert.True(engine.Tend(0));
        Assert.Equal(Crops.Wheat.RegrowSeconds * GameEngine.TendFraction, engine.Tiles[0], precision: 6);
        GrowAll(engine);
        Assert.False(engine.Tend(0));
        Assert.False(engine.Tend(-1));
    }

    [Fact]
    public void Planting_a_new_crop_replants_the_whole_row()
    {
        var engine = NewEngine();
        GrowAll(engine);
        Assert.False(engine.PlantField("carrot")); // locked at Garden Level 1

        engine.State.GardenXp = GardenProgression.TotalXpFor(2);
        Assert.True(engine.PlantField("carrot"));
        Assert.Equal(Crops.Carrot, engine.FieldCrop);
        Assert.All(engine.Tiles, t => Assert.Equal(0, t));
    }

    [Fact]
    public void Expanding_the_field_needs_garden_level_and_coins()
    {
        var engine = NewEngine();
        var first = FieldProgression.Expansions[0];

        engine.State.Coins = 1_000_000;
        Assert.False(engine.TryExpandField(out var reason));
        Assert.Contains("Garden Level", reason);

        engine.State.GardenXp = GardenProgression.TotalXpFor(first.GardenLevel);
        engine.State.Coins = first.Coins - 1;
        Assert.False(engine.TryExpandField(out reason));
        Assert.Contains("coins", reason);

        engine.State.Coins = first.Coins;
        Assert.True(engine.TryExpandField(out _));
        Assert.Equal(first.Length, engine.FieldLength);
        Assert.Equal(first.Length, engine.Tiles.Count);
        Assert.Equal(0, engine.State.Coins);
    }

    [Fact]
    public void The_field_can_be_expanded_to_its_full_length()
    {
        var engine = RichEngine();
        while (engine.TryExpandField(out _)) { }
        Assert.Equal(FieldProgression.MaxLength, engine.FieldLength);
        Assert.False(engine.CanExpandField(out var next, out _));
        Assert.Null(next);
    }

    // ------------------------------------------------------------------ fortune & stats

    [Fact]
    public void Fortune_adds_up_every_source()
    {
        var engine = RichEngine();
        engine.State.FarmingXp = FarmingSkill.TotalXpFor(10);                          // +40
        engine.State.Collections["wheat"] = CollectionTiers.Thresholds(Crops.Wheat)[2]; // tier 3: +6
        engine.State.Tools["wheat"] = new ToolState { Tier = 2, Breaks = 4_500 };     // +30, counter +4
        foreach (var slot in Armour.Slots) engine.State.Armour[slot] = 1;              // 4 x +4
        engine.State.Talismans[TalismanFamily.Clover] = 2;                             // +10

        int expected = 40 + 6 + 30 + 4 + 16 + 10;
        Assert.Equal(expected, engine.GetFortune(Crops.Wheat, handBreak: false));
        Assert.Equal(expected + GameEngine.HandBreakFortuneBonus, engine.GetFortune(Crops.Wheat, handBreak: true));
        Assert.Equal(expected, engine.FortuneSources(Crops.Wheat).Sum(s => s.Amount));

        // The wheat hoe doesn't help carrots.
        Assert.Equal(40 + 16 + 10, engine.GetFortune(Crops.Carrot, handBreak: false));
    }

    [Fact]
    public void Full_armour_sets_give_their_bonus()
    {
        var engine = RichEngine();
        foreach (var slot in Armour.Slots) engine.State.Armour[slot] = 3;
        engine.State.Armour[ArmourSlot.Boots] = 2;

        Assert.Equal(2, engine.FullSetTier); // a full set of Harvester or better
        Assert.Equal(Armour.SetBonus(2).Speed, engine.SpeedPercent);

        engine.State.Armour[ArmourSlot.Boots] = 3;
        Assert.Equal(3, engine.FullSetTier);
        Assert.Equal(Armour.SetBonus(3).Xp, engine.XpPercent);
        Assert.Equal(4 * Armour.Fortune(3) + Armour.SetBonus(3).Fortune, engine.EquipmentBonus.Fortune);
    }

    [Fact]
    public void Talismans_change_speed_sell_price_and_xp()
    {
        var engine = RichEngine();
        engine.State.Talismans[TalismanFamily.Swiftstep] = 3;
        engine.State.Talismans[TalismanFamily.Merchant] = 2;
        engine.State.Talismans[TalismanFamily.Scholar] = 1;

        Assert.Equal(20, engine.SpeedPercent);
        Assert.Equal(10, engine.SellPercent);
        Assert.Equal(5, engine.XpPercent);
        Assert.Equal(GameEngine.BaseFarmerSpeed * 1.2, engine.FarmerSpeed, precision: 6);
        Assert.Equal("Merchant's Ring", Talismans.Name(TalismanFamily.Merchant, 2));
    }

    [Fact]
    public void Merchants_talisman_raises_market_prices()
    {
        var engine = NewEngine();
        engine.State.Talismans[TalismanFamily.Merchant] = 3; // +20%
        engine.State.Settings.KeepForUpgrades = false;
        Give(engine, Items.Raw(Crops.Wheat), 100);

        Assert.Equal(Crops.Wheat.SellPrice * 1.2, engine.MarketPrice(Items.Raw(Crops.Wheat)), precision: 6);
        Assert.Equal(240, engine.CropsValue());
        Assert.Equal(240, engine.Sell("wheat", 100));
    }

    [Fact]
    public void Farming_xp_bonus_boosts_xp_per_break()
    {
        var engine = NewEngine();
        engine.State.Talismans[TalismanFamily.Scholar] = 3; // +20%
        GrowAll(engine);
        var result = engine.Break(0, handBreak: false)!;
        Assert.Equal(Crops.Wheat.FarmingXp * 1.2, result.FarmingXp, precision: 6);
    }

    [Fact]
    public void Hoes_count_the_crops_they_break()
    {
        var engine = NewEngine();
        GrowAll(engine);
        engine.Break(0, handBreak: false);
        Assert.Equal(0, engine.ActiveTool.Breaks); // no hoe yet

        engine.State.Tools["wheat"] = new ToolState { Tier = 1 };
        engine.Break(1, handBreak: false);
        engine.Break(2, handBreak: true);
        Assert.Equal(2, engine.ActiveTool.Breaks);
        Assert.Equal(0, Tools.CounterFortune(999));
        Assert.Equal(1, Tools.CounterFortune(1_000));
        Assert.Equal(Tools.MaxCounterFortune, Tools.CounterFortune(long.MaxValue));
    }

    [Fact]
    public void The_farmer_holds_the_hoe_for_whatever_is_planted()
    {
        var engine = RichEngine();
        engine.State.Tools["wheat"] = new ToolState { Tier = 3 };
        engine.State.Tools["carrot"] = new ToolState { Tier = 1 };

        Assert.Equal(3, engine.ActiveTool.Tier);
        engine.PlantField("carrot");
        Assert.Equal(1, engine.ActiveTool.Tier);
        engine.PlantField("potato");
        Assert.Equal(0, engine.ActiveTool.Tier);
    }

    // ------------------------------------------------------------------ packing & selling

    [Fact]
    public void Packing_turns_160_crops_into_one_packed_crop()
    {
        var engine = NewEngine();
        var wheat = Items.Raw(Crops.Wheat);
        Give(engine, wheat, 400);

        Assert.Equal(2, engine.Pack(wheat.Id));
        Assert.Equal(80, engine.GetItem(wheat));
        Assert.Equal(2, engine.GetItem(Items.Packed(Crops.Wheat)));
        Assert.Equal(0, engine.Pack(wheat.Id)); // not enough left

        Give(engine, Items.Packed(Crops.Wheat), 158);
        Assert.Equal(1, engine.Pack(Items.Packed(Crops.Wheat).Id));
        Assert.Equal(1, engine.GetItem(Items.Dense(Crops.Wheat)));
        Assert.Equal(0, engine.Pack(Items.Dense(Crops.Wheat).Id)); // dense is the top
    }

    [Fact]
    public void Packed_crops_sell_for_the_same_as_their_crops()
    {
        var packed = Items.Packed(Crops.Melon);
        Assert.Equal(160 * Crops.Melon.SellPrice, packed.SellPrice);
        Assert.Equal(160L * 160 * Crops.Melon.SellPrice, Items.Dense(Crops.Melon).SellPrice);
        Assert.Equal("Packed Melon", packed.Name);
        Assert.Equal(36, Items.CropItems.Count());
        Assert.Equal(36 + FishingItems.All.Count, Items.All.Count);
    }

    [Fact]
    public void Selling_all_crops_keeps_packed_crops()
    {
        var engine = NewEngine();
        engine.State.Settings.KeepForUpgrades = false;
        Give(engine, Items.Raw(Crops.Wheat), 10);
        Give(engine, Items.Raw(Crops.Pumpkin), 2);
        Give(engine, Items.Packed(Crops.Wheat), 3);
        int changes = 0;
        engine.Changed += () => changes++;

        long coins = engine.SellAllCrops();

        Assert.Equal(10 * Crops.Wheat.SellPrice + 2 * Crops.Pumpkin.SellPrice, coins);
        Assert.Equal(coins, engine.State.Coins);
        Assert.Equal(3, engine.GetItem(Items.Packed(Crops.Wheat)));
        Assert.Equal(1, changes);

        Assert.Equal(3 * Items.Packed(Crops.Wheat).SellPrice, engine.Sell("packed_wheat", 99));
        Assert.Empty(engine.State.Inventory);
    }

    [Fact]
    public void Auto_sell_pays_coins_instead_of_filling_the_inventory()
    {
        var engine = NewEngine();
        engine.State.Settings.KeepForUpgrades = false;
        engine.SetAutoSell(Crops.Wheat, true);
        GrowAll(engine);

        var result = engine.Break(0, handBreak: false)!;

        Assert.Equal(result.Drops * Crops.Wheat.SellPrice, result.CoinsFromAutoSell);
        Assert.Equal(result.CoinsFromAutoSell, engine.State.Coins);
        Assert.Empty(engine.State.Inventory);
        Assert.Equal(result.Drops, engine.GetCollection(Crops.Wheat));
    }

    // ------------------------------------------------------------------ crafting

    [Fact]
    public void Crafting_a_hoe_uses_up_crops_and_coins()
    {
        var engine = NewEngine();
        var recipe = engine.NextRecipe(UpgradeKind.Tool, "wheat")!;
        Assert.Equal(1, recipe.Tier);
        Assert.Equal("Basic Wheat Sickle", recipe.ResultName);

        Assert.False(engine.TryCraft(recipe, out var reason));
        Assert.Contains("more Wheat", reason);

        Give(engine, Items.Raw(Crops.Wheat), 50);
        Assert.False(engine.TryCraft(recipe, out reason));
        Assert.Contains("coins", reason);

        engine.State.Coins = recipe.Coins;
        var crafted = new List<Recipe>();
        engine.Crafted += crafted.Add;
        Assert.True(engine.TryCraft(recipe, out _));

        Assert.Equal(1, engine.ActiveTool.Tier);
        Assert.Equal(2, engine.GetItem(Items.Raw(Crops.Wheat)));
        Assert.Equal(0, engine.State.Coins);
        Assert.Equal(recipe, Assert.Single(crafted));
        Assert.Equal(2, engine.NextRecipe(UpgradeKind.Tool, "wheat")!.Tier);
    }

    [Fact]
    public void Upgrades_must_be_crafted_in_order()
    {
        var engine = RichEngine();
        Give(engine, Items.Dense(Crops.Wheat), 5);
        var masterwork = Recipes.Find(UpgradeKind.Tool, "wheat", 3)!;

        Assert.False(engine.CanCraft(masterwork, out var reason));
        Assert.Contains("first", reason);

        engine.State.Tools["wheat"] = new ToolState { Tier = 2 };
        Assert.True(engine.TryCraft(masterwork, out _));
        Assert.Null(engine.NextRecipe(UpgradeKind.Tool, "wheat"));
        Assert.False(engine.CanCraft(masterwork, out reason));
        Assert.Equal("Already owned", reason);
    }

    [Fact]
    public void Upgrades_can_need_a_garden_level()
    {
        var engine = RichEngine(gardenLevel: 1);
        Give(engine, Items.Packed(Crops.Wheat), 10);
        var helmet = engine.NextRecipe(UpgradeKind.Armour, nameof(ArmourSlot.Helmet))!;

        Assert.False(engine.CanCraft(helmet, out var reason));
        Assert.Contains("Garden Level", reason);

        engine.State.GardenXp = GardenProgression.TotalXpFor(helmet.GardenLevel);
        Assert.True(engine.TryCraft(helmet, out _));
        Assert.Equal(1, engine.GetArmourTier(ArmourSlot.Helmet));
        Assert.Equal("Straw Helmet", helmet.ResultName);
    }

    [Fact]
    public void Talismans_upgrade_from_charm_to_relic()
    {
        var engine = RichEngine();
        foreach (var item in Items.All) Give(engine, item, 1_000);

        for (int tier = 1; tier <= Talismans.MaxTier; tier++)
        {
            var recipe = engine.NextRecipe(UpgradeKind.Talisman, nameof(TalismanFamily.Clover))!;
            Assert.Equal(tier, recipe.Tier);
            Assert.True(engine.TryCraft(recipe, out var reason), reason);
        }

        Assert.Equal(3, engine.GetTalismanTier(TalismanFamily.Clover));
        Assert.Equal(20, engine.EquipmentBonus.Fortune);
        Assert.Equal("Clover Relic", Talismans.Name(TalismanFamily.Clover, 3));
        Assert.Null(engine.NextRecipe(UpgradeKind.Talisman, nameof(TalismanFamily.Clover)));
    }

    [Fact]
    public void Every_upgrade_has_three_tiers_with_real_ingredients()
    {
        var targets = Crops.All.Select(c => (UpgradeKind.Tool, c.Id))
            .Concat(Armour.Slots.Select(s => (UpgradeKind.Armour, s.ToString())))
            .Concat(Talismans.Families.Select(f => (UpgradeKind.Talisman, f.ToString())));

        foreach (var (kind, target) in targets)
        {
            for (int tier = 1; tier <= 3; tier++)
            {
                var recipe = Recipes.Find(kind, target, tier);
                Assert.NotNull(recipe);
                Assert.NotEmpty(recipe!.Ingredients);
                Assert.All(recipe.Ingredients, i => Assert.True(Items.TryGet(i.ItemId, out _), i.ItemId));
                Assert.InRange(recipe.GardenLevel, 1, GardenProgression.MaxLevel);
                Assert.True(recipe.Coins > 0);
            }
            Assert.Null(Recipes.Find(kind, target, 4));
        }
        Assert.Equal(Recipes.All.Count, Recipes.All.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Everything_can_be_crafted_with_enough_crops()
    {
        var engine = RichEngine();
        engine.State.Fishing.Xp = FishingSkill.TotalXpFor(FishingSkill.MaxLevel);
        foreach (var item in Items.All) Give(engine, item, 100_000);

        foreach (var recipe in Recipes.All.OrderBy(r => r.Tier))
            Assert.True(engine.TryCraft(recipe, out var reason), $"{recipe.ResultName}: {reason}");

        Assert.Equal(Recipes.All.Count, engine.State.Stats.ItemsCrafted);
        Assert.Equal(3, engine.FullSetTier);
    }

    // ------------------------------------------------------------------ levels

    [Fact]
    public void Farming_level_up_raises_event_and_pays_coins()
    {
        var engine = NewEngine();
        engine.State.FarmingXp = FarmingSkill.TotalXpFor(1) - 1;
        var levelUps = new List<LevelUp>();
        engine.LeveledUp += levelUps.Add;

        GrowAll(engine);
        engine.Break(0, handBreak: false);

        var farming = Assert.Single(levelUps, l => l.Skill == SkillKind.Farming);
        Assert.Equal(1, farming.NewLevel);
        Assert.Equal(FarmingSkill.CoinRewardPerLevel, engine.State.Coins);
    }

    [Fact]
    public void Garden_level_up_announces_what_it_unlocked()
    {
        var engine = NewEngine();
        engine.State.GardenXp = GardenProgression.TotalXpFor(2) - 1;
        var levelUps = new List<LevelUp>();
        engine.LeveledUp += levelUps.Add;

        GrowAll(engine);
        engine.Break(0, handBreak: false);

        var garden = Assert.Single(levelUps, l => l.Skill == SkillKind.Garden);
        Assert.Equal(2, garden.NewLevel);
        Assert.Contains("Unlocked Carrot", garden.Rewards);
        Assert.Contains(garden.Rewards, r => r.StartsWith("Field expansion available"));
        Assert.Contains(garden.Rewards, r => r.Contains("new upgrade"));
    }

    [Fact]
    public void Reaching_a_collection_tier_gives_garden_xp()
    {
        var engine = NewEngine(roll: 0.99);
        engine.State.Collections["wheat"] = CollectionTiers.Thresholds(Crops.Wheat)[0] - 1;
        var tiers = new List<CollectionTierUp>();
        engine.CollectionTierReached += tiers.Add;

        GrowAll(engine);
        var result = engine.Break(0, handBreak: false)!;

        Assert.Equal(1, Assert.Single(tiers).NewTier);
        Assert.Equal(GardenProgression.XpPerBreak + CollectionTiers.GardenXpPerTier, result.GardenXp);
    }

    // ------------------------------------------------------------------ offline

    [Fact]
    public void Offline_progress_breaks_crops_at_reduced_speed()
    {
        var engine = NewEngine(roll: 0.99);
        double rate = engine.OfflineBreaksPerSecond();
        // The whole row once per lap: 11 crops of walking at 2.5 a second, plus the teleport.
        double lap = 11 / GameEngine.BaseFarmerSpeed + FarmerAgent.TeleportDuration;
        Assert.Equal(lap, engine.LapSeconds, precision: 6);
        Assert.Equal(10 / lap * GameEngine.OfflineEfficiency, rate, precision: 6);

        var report = engine.ApplyOfflineProgress(TimeSpan.FromMinutes(10));

        Assert.Equal((long)(600 * rate), report.Breaks);
        Assert.True(report.Drops >= report.Breaks);
        Assert.Equal(engine.State.FarmingXp, report.FarmingXp, precision: 6);
        Assert.True(report.FarmingLevelAfter > report.FarmingLevelBefore);
        Assert.Equal(engine.FieldLength, engine.GrownCount);
    }

    [Fact]
    public void Offline_progress_is_capped_and_quiet()
    {
        var engine = NewEngine();
        int events = 0;
        engine.Broke += _ => events++;
        engine.LeveledUp += _ => events++;

        var report = engine.ApplyOfflineProgress(TimeSpan.FromDays(30));

        Assert.Equal(GameEngine.MaxOfflineTime, report.Counted);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Offline_without_auto_harvest_only_grows_crops()
    {
        var engine = NewEngine();
        engine.State.Settings.FarmerAutoHarvest = false;
        var report = engine.ApplyOfflineProgress(TimeSpan.FromHours(1));
        Assert.True(report.IsEmpty);
        Assert.Equal(engine.FieldLength, engine.GrownCount);
    }

    [Fact]
    public void Reset_wipes_progress_but_keeps_settings()
    {
        var engine = RichEngine();
        foreach (var item in Items.All) Give(engine, item, 100_000);
        foreach (var recipe in Recipes.All.OrderBy(r => r.Tier)) engine.TryCraft(recipe, out _);
        engine.TryExpandField(out _);
        engine.State.Settings.StripMode = StripMode.OnHover;

        engine.ResetProgress();

        Assert.Equal(0, engine.State.Coins);
        Assert.Equal(1, engine.GardenLevel);
        Assert.Empty(engine.State.Inventory);
        Assert.Equal(FieldProgression.StartLength, engine.FieldLength);
        Assert.True(engine.EquipmentBonus.IsEmpty);
        Assert.Equal(0, engine.ActiveTool.Tier);
        Assert.Equal(StripMode.OnHover, engine.State.Settings.StripMode);
    }

    // ------------------------------------------------------------------ balance

    [Fact]
    public void Each_new_crop_is_worth_more()
    {
        for (int i = 1; i < Crops.All.Count; i++)
        {
            var (a, b) = (Crops.All[i - 1], Crops.All[i]);
            Assert.True(b.CoinsPerBreak > a.CoinsPerBreak, $"{b.Name} should be worth more per break than {a.Name}");
            Assert.True(b.UnlockGardenLevel > a.UnlockGardenLevel);
            Assert.True(b.RegrowSeconds >= a.RegrowSeconds);
        }
        Assert.Equal(Crops.All.Count, Crops.All.Select(c => c.Id).Distinct().Count());
    }
}
