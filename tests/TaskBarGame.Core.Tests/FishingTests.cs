using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>Returns the given numbers in turn, then keeps repeating the last one.</summary>
internal sealed class SequenceRandom(params double[] values) : Random
{
    private int _next;

    public override double NextDouble() => values[Math.Min(_next++, values.Length - 1)];
}

public class FishingTests
{
    private static GameEngine Engine(Random rng, int fishingLevel = 0)
    {
        var engine = new GameEngine(GameState.CreateNew(), rng);
        engine.State.Fishing.Xp = FishingSkill.TotalXpFor(fishingLevel);
        return engine;
    }

    private static GameEngine AtWillowLake(Random rng, int fishingLevel = 0)
    {
        var engine = Engine(rng, fishingLevel);
        Assert.True(engine.TravelTo(Places.WillowLake.Id, out _));
        return engine;
    }

    private static void Give(GameEngine engine, FishingItemDef item, long count) =>
        engine.State.Inventory[item.Id] = engine.GetItem(item.Id) + count;

    // ================================================================= the data

    [Fact]
    public void Every_fishing_item_has_its_own_id_and_everything_points_at_real_items()
    {
        var ids = FishingItems.All.Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(Crops.TryGet(id, out _), $"{id} is also a crop id"));
        Assert.All(FishingItems.All, i => Assert.True(Items.TryGet(i.Id, out var item) && !item.IsCrop));

        var referenced = Places.Lakes.SelectMany(l => l.Catches.Select(c => c.ItemId))
            .Concat(Places.Treasure.Select(t => t.ItemId))
            .Concat(Trades.All.SelectMany(t => t.Cost.Select(c => c.ItemId)))
            .Concat(Trades.All.Where(t => t.ResultItemId is not null).Select(t => t.ResultItemId!))
            .Concat(Recipes.All.SelectMany(r => r.Ingredients.Select(i => i.ItemId)));
        Assert.All(referenced, id => Assert.True(Items.TryGet(id, out _), $"{id} isn't an item"));
    }

    [Fact]
    public void Every_fishing_item_rod_armour_piece_and_the_lake_have_pictures()
    {
        var sheet = SpriteSheet.LoadDefault();
        Assert.All(FishingItems.All, i => Assert.True(sheet.Contains(i.Sprite), $"{i.Name} has no '{i.Sprite}' sprite"));
        Assert.All(Armour.Slots, s => Assert.True(sheet.Contains(FishingArmour.Sprite(s))));
        Assert.All(new[] { "rod", "water1", "water2", "pontoon", "bobber", "hat_souwester", "hat_buckethat" },
            name => Assert.True(sheet.Contains(name), name));
    }

    [Fact]
    public void Every_lake_has_something_that_bites_at_level_0()
    {
        Assert.All(Places.Lakes, lake => Assert.Contains(lake.Catches, c => c.FishingLevel == 0));
        Assert.Equal(0, Places.WillowLake.BoatTripCost);
        Assert.True(Places.DriftwoodCove.IsOnAnotherIsland);
        Assert.Contains(Places.DriftwoodCove.Catches, c => c.Item.Kind == ItemKind.Junk);
        Assert.DoesNotContain(Places.WillowLake.Catches, c => c.Item.Kind == ItemKind.Junk);
        Assert.Contains(Places.WillowLake.Catches, c => c.Item.Kind == ItemKind.LakeDrop);
    }

    [Fact]
    public void Each_rod_is_better_than_the_last_with_more_treasure_chance()
    {
        int[] levels = { 0, 10, 15, 20, 24 };
        for (int tier = 1; tier <= Rods.MaxTier; tier++)
        {
            Assert.Equal(levels[tier - 1], Rods.FishingLevel(tier));
            if (tier == 1) continue;
            Assert.True(Rods.Bonus(tier).Speed > Rods.Bonus(tier - 1).Speed);
            Assert.True(Rods.Bonus(tier).Treasure > Rods.Bonus(tier - 1).Treasure);
            Assert.True(Rods.Rarity(tier) > Rods.Rarity(tier - 1));
            Assert.NotNull(Recipes.Find(UpgradeKind.Rod, Rods.Target, tier));
        }
        Assert.Null(Recipes.Find(UpgradeKind.Rod, Rods.Target, 1)); // everyone starts with one
        Assert.Equal(new[] { 5, 35, 50, 65, 80 }, Enumerable.Range(1, 5).Select(t => Rods.Bonus(t).Speed));
    }

    [Fact]
    public void Treasure_is_used_to_craft_rods_and_fishing_armour()
    {
        var treasure = FishingItems.All.Where(i => i.Kind == ItemKind.Treasure).Select(i => i.Id).ToHashSet();
        var fishingRecipes = Recipes.All.Where(r => r.IsFishing).ToList();
        Assert.All(treasure, id => Assert.Contains(fishingRecipes, r => r.Ingredients.Any(i => i.ItemId == id)));
        Assert.All(fishingRecipes.Where(r => r.Kind == UpgradeKind.Rod),
            r => Assert.Contains(r.Ingredients, i => treasure.Contains(i.ItemId)));
        Assert.All(fishingRecipes.Where(r => r.Kind == UpgradeKind.FishingArmour && r.Tier < 3),
            r => Assert.Contains(r.Ingredients, i => treasure.Contains(i.ItemId)));
    }

    // ================================================================= going places

    [Fact]
    public void You_start_at_the_field_with_a_Fishing_Rod_and_catch_nothing_there()
    {
        var engine = Engine(new FixedRandom(0.5));
        Assert.Equal(Places.Field, engine.Location);
        Assert.False(engine.IsFishing);
        Assert.Equal(1, engine.RodTier);
        Assert.Null(engine.Catch());
    }

    [Fact]
    public void Willow_Lake_is_open_but_Driftwood_Cove_needs_a_boat_trip()
    {
        var engine = Engine(new FixedRandom(0.5));
        int travelled = 0;
        engine.Travelled += () => travelled++;

        Assert.True(engine.TravelTo(Places.WillowLake.Id, out _));
        Assert.Equal(Places.WillowLake, engine.CurrentLake);

        Assert.False(engine.TravelTo(Places.DriftwoodCove.Id, out string reason));
        Assert.Contains("boat trip", reason);
        Assert.False(engine.BuyBoatTrip(Places.DriftwoodCove, out reason));
        Assert.Contains("more coins", reason);

        engine.State.Coins = 60_000;
        Assert.True(engine.BuyBoatTrip(Places.DriftwoodCove, out _));
        Assert.Equal(10_000, engine.State.Coins);
        Assert.True(engine.TravelTo(Places.DriftwoodCove.Id, out _));
        Assert.True(engine.TravelTo(Places.Field, out _));
        Assert.Equal(3, travelled);
    }

    // ================================================================= catching

    [Fact]
    public void A_catch_goes_into_the_inventory_and_catch_log_and_gives_Fishing_XP()
    {
        // 0.99: no treasure, and the last thing that bites at level 0 (Clay).
        var engine = AtWillowLake(new FixedRandom(0.99));
        CatchResult? caught = null;
        engine.Caught += c => caught = c;

        var result = engine.Catch()!;

        Assert.Same(caught, result);
        Assert.Equal(FishingItems.Clay.Id, result.Item.Id);
        Assert.Equal(1, result.Count);
        Assert.False(result.Treasure);
        Assert.True(result.First);
        Assert.Equal(1, engine.GetItem(FishingItems.Clay.Id));
        Assert.Equal(1, engine.TimesCaught(result.Item));
        Assert.Equal(6, engine.State.Fishing.Xp);
        Assert.False(engine.Catch()!.First);
    }

    [Fact]
    public void Fish_only_bite_once_your_Fishing_level_is_high_enough()
    {
        var low = AtWillowLake(new FixedRandom(0.5));
        var chances = low.BiteChances(Places.WillowLake);
        Assert.All(chances.Where(c => c.Entry.FishingLevel > 0), c => Assert.Equal(0, c.Percent));
        Assert.Equal(100 - low.TreasureChance, chances.Sum(c => c.Percent), 6);

        var high = AtWillowLake(new FixedRandom(0.99999), fishingLevel: 20);
        Assert.All(high.BiteChances(Places.WillowLake), c => Assert.True(c.Unlocked));
        Assert.Equal(FishingItems.GoldenCarp.Id, high.Catch()!.Item.Id); // the last one in the list
    }

    [Fact]
    public void A_lucky_roll_is_treasure()
    {
        var engine = AtWillowLake(new FixedRandom(0.0));
        var result = engine.Catch()!;
        Assert.True(result.Treasure);
        Assert.Equal(ItemKind.Treasure, result.Item.Kind);
        Assert.Equal(1, engine.State.Fishing.Treasures);
        Assert.Equal(GameEngine.BaseTreasureChance + Rods.Bonus(1).Treasure, engine.TreasureChance);
    }

    [Fact]
    public void Fishing_Speed_makes_fish_bite_sooner()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        Assert.Equal(5, engine.FishingSpeed); // just the Fishing Rod
        Assert.Equal(GameEngine.BaseWaitSeconds / 1.05, engine.AverageWaitSeconds, 6);

        engine.State.Fishing.Xp = FishingSkill.TotalXpFor(10);
        engine.State.Fishing.Rod = 2;
        Assert.Equal(10 + 35, engine.FishingSpeed);
        Assert.Equal(GameEngine.BaseWaitSeconds / 1.45, engine.AverageWaitSeconds, 6);
        Assert.Equal(3600 / engine.CatchCycleSeconds, engine.CatchesPerHour, 6);
    }

    [Fact]
    public void Bait_is_used_up_one_per_catch_and_stops_helping_when_it_runs_out()
    {
        var engine = AtWillowLake(new FixedRandom(0.99));
        Give(engine, FishingItems.WrigglerBait, 2);
        engine.SetBait(FishingItems.WrigglerBait.Id);
        Assert.Equal(5 + 30, engine.FishingSpeed);

        Assert.Equal(FishingItems.WrigglerBait.Id, engine.Catch()!.Bait!.Id);
        engine.Catch();
        Assert.Equal(0, engine.GetItem(FishingItems.WrigglerBait.Id));
        Assert.Null(engine.ActiveBait);
        Assert.Equal(FishingItems.WrigglerBait.Id, engine.ChosenBait!.Id); // still chosen for when you get more
        Assert.Equal(5, engine.FishingSpeed);
        Assert.Null(engine.Catch()!.Bait);
    }

    [Fact]
    public void Glimmer_Bait_adds_Treasure_Chance()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        double before = engine.TreasureChance;
        Give(engine, FishingItems.GlimmerBait, 1);
        engine.SetBait(FishingItems.GlimmerBait.Id);
        Assert.Equal(before + 3, engine.TreasureChance, 6);
    }

    [Fact]
    public void Twin_Bait_can_catch_two_at_once()
    {
        // No treasure (0.5), the first thing on the list (Perch), then a double catch (0.1 < 20%).
        var engine = AtWillowLake(new SequenceRandom(0.5, 0.0, 0.1));
        Give(engine, FishingItems.TwinBait, 1);
        engine.SetBait(FishingItems.TwinBait.Id);

        var result = engine.Catch()!;

        Assert.Equal(FishingItems.Perch.Id, result.Item.Id);
        Assert.Equal(2, result.Count);
        Assert.Equal(2, engine.GetItem(FishingItems.Perch.Id));
        Assert.Equal(24, result.FishingXp);
    }

    [Fact]
    public void Fishing_levels_give_Fishing_Speed_and_coins()
    {
        var engine = AtWillowLake(new FixedRandom(0.99));
        engine.State.Fishing.Xp = FishingSkill.TotalXpFor(1) - 1;
        LevelUp? levelUp = null;
        engine.LeveledUp += l => levelUp = l;

        engine.Catch();

        Assert.NotNull(levelUp);
        Assert.Equal(SkillKind.Fishing, levelUp!.Skill);
        Assert.Equal(1, engine.FishingLevel);
        Assert.Equal(FishingSkill.CoinRewardPerLevel, engine.State.Coins);
        Assert.Equal(5 + 1, engine.FishingSpeed);
    }

    // ================================================================= crafting

    [Fact]
    public void Better_rods_need_a_Fishing_level_fish_and_treasure()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        var recipe = engine.NextRecipe(UpgradeKind.Rod, Rods.Target)!;
        Assert.Equal(2, recipe.Tier);
        Assert.Equal("Needs Fishing Level 10", engine.CraftBlocker(recipe));

        engine.State.Fishing.Xp = FishingSkill.TotalXpFor(10);
        Assert.Null(engine.CraftBlocker(recipe));
        Assert.False(engine.CanCraft(recipe, out _));

        foreach (var ingredient in recipe.Ingredients) engine.State.Inventory[ingredient.ItemId] = ingredient.Count;
        engine.State.Coins = recipe.Coins;
        Assert.True(engine.TryCraft(recipe, out _));
        Assert.Equal(2, engine.RodTier);
        Assert.Equal("Angler's Rod", recipe.ResultName);
        Assert.Equal(10 + 35, engine.FishingSpeed);
    }

    [Fact]
    public void A_full_fishing_armour_set_gives_its_set_bonus_and_only_fishing_stats()
    {
        var engine = AtWillowLake(new FixedRandom(0.5), fishingLevel: 5);
        var farmingBefore = engine.TotalBonus;
        foreach (var slot in Armour.Slots)
        {
            var recipe = engine.NextRecipe(UpgradeKind.FishingArmour, slot.ToString())!;
            foreach (var ingredient in recipe.Ingredients) engine.State.Inventory[ingredient.ItemId] = ingredient.Count;
            engine.State.Coins = recipe.Coins;
            Assert.True(engine.TryCraft(recipe, out string reason), reason);
        }

        Assert.Equal(1, engine.FullFishingSetTier);
        Assert.Equal("Oilskin Coat", FishingArmour.Name(ArmourSlot.Chestplate, 1));
        Assert.Equal(5 + 5 + 4 * 2 + 5, engine.FishingSpeed); // rod, levels, pieces, set bonus
        Assert.Equal(farmingBefore, engine.TotalBonus);
    }

    [Fact]
    public void Fishing_items_your_next_upgrades_need_are_kept_from_the_Market()
    {
        var engine = AtWillowLake(new FixedRandom(0.5), fishingLevel: 10);
        Give(engine, FishingItems.Carp, 200);
        // The Angler's Rod needs 80 and the Oilskin Coat 30.
        Assert.Equal(80 + 30, engine.KeptForUpgrades(Items.Of(FishingItems.Carp)));
        engine.Sell(FishingItems.Carp.Id, long.MaxValue);
        Assert.Equal(110, engine.GetItem(FishingItems.Carp.Id));
    }

    // ================================================================= trades

    [Fact]
    public void The_bait_table_turns_fish_into_bait()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        var wriggler = Trades.Find("bait_wriggler")!;
        Give(engine, FishingItems.Perch, 3);
        Give(engine, FishingItems.LilyPad, 2);

        Assert.Equal(2, engine.MaxTrades(wriggler));
        Assert.Equal(2, engine.Trade(wriggler, 10, out _));
        Assert.Equal(50, engine.GetItem(FishingItems.WrigglerBait.Id));
        Assert.Equal(1, engine.GetItem(FishingItems.Perch.Id));

        Assert.False(engine.CanTrade(Trades.Find("bait_twin")!, out string reason));
        Assert.Equal("Needs Fishing Level 8", reason);
    }

    [Fact]
    public void Rusty_Rowans_shop_opens_with_the_boat_trip_and_takes_junk()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        var hook = Trades.Find("junk_hook")!;
        Give(engine, FishingItems.TinCan, 16);
        Give(engine, FishingItems.BrokenBottle, 8);
        Assert.False(engine.CanTrade(hook, out string reason));
        Assert.Contains("boat trip", reason);

        engine.State.Coins = Places.DriftwoodCove.BoatTripCost;
        engine.BuyBoatTrip(Places.DriftwoodCove, out _);
        Assert.True(engine.IsJunkShopOpen);
        Assert.Equal(2, engine.Trade(hook, 5, out _));
        Assert.Equal(2, engine.GetItem(FishingItems.BrassHook.Id));
    }

    [Fact]
    public void Rowans_cosmetics_can_only_be_bought_once_and_unlock_in_the_wardrobe()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        engine.State.Fishing.BoatTrips.Add(Places.DriftwoodCove.Id);
        var hat = Trades.Find("junk_buckethat")!;
        Give(engine, FishingItems.SoggyBoot, 100);
        Give(engine, FishingItems.RustyAnchor, 10);

        Assert.False(Cosmetics.Find("buckethat")!.IsUnlocked(engine));
        Assert.Equal(1, engine.MaxTrades(hat));
        Assert.Equal(1, engine.Trade(hat, 5, out _));
        Assert.True(Cosmetics.Find("buckethat")!.IsUnlocked(engine));
        Assert.Equal(0, engine.Trade(hat, 1, out string reason));
        Assert.Equal("Already yours", reason);
    }

    // ================================================================= time passing

    [Fact]
    public void While_the_game_is_closed_the_farmer_keeps_fishing_at_half_speed_and_the_field_just_grows()
    {
        var engine = AtWillowLake(new FixedRandom(0.99));
        double cycle = engine.CatchCycleSeconds;

        var report = engine.ApplyOfflineProgress(TimeSpan.FromHours(1));

        Assert.Equal((long)Math.Floor(3600 / cycle * GameEngine.OfflineEfficiency), report.Catches);
        Assert.True(report.FishingXp > 0);
        Assert.Equal(0, report.Breaks);
        Assert.False(report.IsEmpty);
        Assert.Equal(engine.FieldLength, engine.GrownCount);
        Assert.Equal(report.Catches, engine.State.Fishing.Catches);
    }

    [Fact]
    public void The_angler_casts_waits_and_reels_in_by_themselves()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        var angler = new AnglerAgent(new FixedRandom(0.5));
        var caught = new List<CatchResult>();
        angler.Caught += caught.Add;

        var phases = new HashSet<AnglerPhase>();
        for (int i = 0; i < 10 * 60 * 10; i++) // 10 minutes in tenths of a second
        {
            angler.Tick(0.1, engine);
            phases.Add(angler.Phase);
        }

        Assert.Equal(Enum.GetValues<AnglerPhase>().ToHashSet(), phases);
        Assert.InRange(caught.Count, 600 / engine.CatchCycleSeconds - 3, 600 / engine.CatchCycleSeconds + 3);
        Assert.InRange(angler.BobberAt, AnglerAgent.NearestCast, engine.FieldLength);
    }

    [Fact]
    public void The_angler_does_nothing_at_the_field()
    {
        var engine = Engine(new FixedRandom(0.5));
        var angler = new AnglerAgent();
        int caught = 0;
        angler.Caught += _ => caught++;
        for (int i = 0; i < 1000; i++) angler.Tick(0.5, engine);
        Assert.Equal(0, caught);
    }

    [Fact]
    public void Fishing_items_trade_on_the_Bazaar()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        var pike = Items.Of(FishingItems.Pike);
        Assert.Equal(GameEngine.BazaarBasePrice(pike), engine.BazaarMidPrice(pike));
        Give(engine, FishingItems.Pike, 500);
        double before = engine.BazaarSellPrice(pike);
        Assert.True(engine.BazaarSell(pike.Id, 500, out _) > 0);
        Assert.True(engine.BazaarSellPrice(pike) < before);
    }

    // ================================================================= fish collections

    [Fact]
    public void Every_fish_has_a_collection_and_rarer_fish_need_fewer()
    {
        Assert.Equal(FishingItems.All.Count(i => i.Kind == ItemKind.Fish), FishCollections.Fish.Count);
        Assert.DoesNotContain(FishCollections.Fish, f => f.Kind != ItemKind.Fish);
        foreach (var fish in FishCollections.Fish)
        {
            var thresholds = FishCollections.Thresholds(fish);
            Assert.Equal(FishCollections.MaxTier, thresholds.Count);
            for (int i = 1; i < thresholds.Count; i++) Assert.True(thresholds[i] > thresholds[i - 1], fish.Name);
        }
        Assert.Equal(10, FishCollections.Thresholds(FishingItems.Perch)[0]);
        Assert.True(FishCollections.MaxThreshold(FishingItems.GoldenCarp) < FishCollections.MaxThreshold(FishingItems.Carp));
        Assert.True(FishCollections.MaxThreshold(FishingItems.Swordfish) < FishCollections.MaxThreshold(FishingItems.Mackerel));
    }

    [Fact]
    public void Reaching_a_fish_collection_tier_gives_Fishing_XP_and_Treasure_Chance()
    {
        // No treasure (0.5), then Perch, the first thing on the list (0.0).
        var engine = AtWillowLake(new SequenceRandom(0.5, 0.0, 0.99));
        engine.State.Fishing.Caught[FishingItems.Perch.Id] = 9;
        double treasureBefore = engine.TreasureChance;
        FishCollectionTierUp? tierUp = null;
        engine.FishCollectionTierReached += t => tierUp = t;

        var result = engine.Catch()!;

        Assert.Equal(FishingItems.Perch.Id, result.Item.Id);
        Assert.NotNull(tierUp);
        Assert.Equal(1, tierUp!.NewTier);
        Assert.Equal(1, engine.FishCollectionTier(FishingItems.Perch));
        Assert.Equal(12 + FishCollections.FishingXpPerTier, engine.State.Fishing.Xp);
        Assert.Equal(treasureBefore + FishCollections.TreasurePerTier, engine.TreasureChance, 6);
    }

    [Fact]
    public void Lake_drops_and_treasure_have_no_collection()
    {
        var engine = AtWillowLake(new FixedRandom(0.99)); // Clay
        engine.State.Fishing.Caught[FishingItems.Clay.Id] = 1_000_000;
        int fired = 0;
        engine.FishCollectionTierReached += _ => fired++;
        engine.Catch();
        Assert.Equal(0, fired);
        Assert.Equal(0, engine.FishCollectionTiers);
    }

    // ================================================================= small fixes

    [Fact]
    public void Fishing_items_can_be_auto_sold_as_theyre_caught_but_not_bait()
    {
        var engine = AtWillowLake(new FixedRandom(0.99)); // Clay
        engine.SetAutoSell(Items.Of(FishingItems.Clay), true);
        engine.SetAutoSell(Items.Of(FishingItems.WrigglerBait), true);
        Assert.False(engine.IsAutoSelling(Items.Of(FishingItems.WrigglerBait)));

        var result = engine.Catch()!;

        Assert.Equal(40, result.AutoSoldCoins);
        Assert.Equal(0, engine.GetItem(FishingItems.Clay.Id));
        Assert.Equal(40, engine.State.Coins);
        Assert.Equal(1, engine.TimesCaught(Items.Of(FishingItems.Clay))); // still in the catch log
    }

    [Fact]
    public void Auto_sell_keeps_what_your_next_upgrades_need()
    {
        var engine = AtWillowLake(new SequenceRandom(0.5, 0.3, 0.99), fishingLevel: 10); // Carp
        Give(engine, FishingItems.Carp, 50);
        engine.SetAutoSell(Items.Of(FishingItems.Carp), true);
        engine.Catch();
        Assert.Equal(51, engine.GetItem(FishingItems.Carp.Id)); // the Angler's Rod and Oilskin Coat need 110
    }

    [Fact]
    public void The_catch_that_uses_the_last_bait_says_so()
    {
        var engine = AtWillowLake(new FixedRandom(0.99));
        Give(engine, FishingItems.WrigglerBait, 2);
        engine.SetBait(FishingItems.WrigglerBait.Id);
        Assert.False(engine.Catch()!.BaitRanOut);
        Assert.True(engine.Catch()!.BaitRanOut);
        Assert.False(engine.Catch()!.BaitRanOut); // no bait left to run out
    }

    [Fact]
    public void The_welcome_back_report_lists_what_was_caught_and_any_bait_that_ran_out()
    {
        var engine = AtWillowLake(new FixedRandom(0.99));
        Give(engine, FishingItems.WrigglerBait, 3);
        engine.SetBait(FishingItems.WrigglerBait.Id);

        var report = engine.ApplyOfflineProgress(TimeSpan.FromHours(1));

        Assert.Equal(report.FishCaught, report.Caught.Values.Sum());
        Assert.True(report.Caught[FishingItems.Clay.Id] > 0);
        Assert.Equal("Wriggler Bait", report.BaitRanOut);
    }

    [Fact]
    public void Ready_junk_trades_leave_out_bait()
    {
        var engine = AtWillowLake(new FixedRandom(0.5));
        engine.State.Fishing.BoatTrips.Add(Places.DriftwoodCove.Id);
        Give(engine, FishingItems.TinCan, 100);
        Assert.Empty(engine.ReadyJunkTrades()); // only bait so far
        Give(engine, FishingItems.BrokenBottle, 4);
        Assert.Equal("junk_hook", Assert.Single(engine.ReadyJunkTrades()).Id);
    }

    [Fact]
    public void A_save_forgets_auto_sell_for_bait_crops_and_unknown_items()
    {
        var state = GameState.CreateNew();
        state.Settings.AutoSellItems.UnionWith(new[] { "perch", "wrigglerbait", "wheat", "nonsense" });
        state.Normalize();
        Assert.Equal(new[] { "perch" }, state.Settings.AutoSellItems);
    }

    [Fact]
    public void A_save_with_a_strange_place_or_a_cove_without_a_boat_trip_goes_back_to_the_field()
    {
        var state = GameState.CreateNew();
        state.Fishing.Location = "atlantis";
        state.Fishing.Rod = 99;
        state.Fishing.Bait = "wheat";
        state.Fishing.Caught["wheat"] = 5;
        state.Normalize();
        Assert.Equal(Places.Field, state.Fishing.Location);
        Assert.Equal(Rods.MaxTier, state.Fishing.Rod);
        Assert.Null(state.Fishing.Bait);
        Assert.Empty(state.Fishing.Caught);

        state.Fishing.Location = Places.DriftwoodCove.Id;
        state.Normalize();
        Assert.Equal(Places.Field, state.Fishing.Location);
    }
}
