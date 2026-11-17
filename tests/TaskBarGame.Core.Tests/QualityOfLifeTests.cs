using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>Packing amounts, the Compactor, auto-sell per crop, locks, keeping crops for upgrades and more.</summary>
public class QualityOfLifeTests
{
    private static readonly ItemDef Wheat = Items.Raw(Crops.Wheat);
    private static readonly ItemDef PackedWheat = Items.Packed(Crops.Wheat);
    private static readonly ItemDef DenseWheat = Items.Dense(Crops.Wheat);

    private static GameEngine NewEngine(int gardenLevel = 1, long coins = 0)
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.99));
        engine.State.GardenXp = GardenProgression.TotalXpFor(gardenLevel);
        engine.State.Coins = coins;
        return engine;
    }

    private static void Give(GameEngine engine, ItemDef item, long count) =>
        engine.State.Inventory[item.Id] = engine.GetItem(item) + count;

    /// <summary>Grows the row and has the farmer break the first crop.</summary>
    private static BreakResult BreakOne(GameEngine engine, int tile = 0)
    {
        engine.Tick(1_000);
        return engine.Break(tile, handBreak: false)!;
    }

    // ------------------------------------------------------------------ packing

    [Fact]
    public void You_can_choose_how_many_to_pack()
    {
        var engine = NewEngine();
        Give(engine, Wheat, 1_000);

        Assert.Equal(2, engine.Pack(Wheat.Id, 2));
        Assert.Equal(680, engine.GetItem(Wheat));
        Assert.Equal(2, engine.GetItem(PackedWheat));

        Assert.Equal(4, engine.Pack(Wheat.Id, 50)); // only 4 more fit
        Assert.Equal(0, engine.Pack(Wheat.Id, 0));
    }

    // ------------------------------------------------------------------ keeping crops for upgrades

    [Fact]
    public void Market_selling_keeps_what_the_next_upgrades_need()
    {
        var engine = NewEngine(); // Garden 1: the next upgrade is a Basic Wheat Sickle (48 Wheat)
        Give(engine, Wheat, 100);

        Assert.Equal(48, engine.KeptForUpgrades(Wheat));
        Assert.Equal(52, engine.SellableAmount(Wheat));
        Assert.Equal(52 * Crops.Wheat.SellPrice, engine.CropsValue());
        Assert.Equal(52 * Crops.Wheat.SellPrice, engine.SellAllCrops());
        Assert.Equal(48, engine.GetItem(Wheat));
        Assert.Equal(0, engine.Sell(Wheat.Id, 10));

        engine.State.Settings.KeepForUpgrades = false;
        Assert.Equal(48 * Crops.Wheat.SellPrice, engine.Sell(Wheat.Id, 48));
    }

    [Fact]
    public void Kept_crops_include_the_ones_you_still_have_to_pack()
    {
        var engine = NewEngine(gardenLevel: 2);
        Give(engine, PackedWheat, 5);

        // Garden 2: Straw armour (2 + 4 + 3 + 2 Packed Wheat), a Scholar's Charm (4 Packed Wheat)
        // and a Basic Wheat Sickle (48 Wheat). You have 5 of the 15 Packed Wheat, so 10 more to pack.
        var needs = engine.UpgradeNeeds(Crops.Wheat);
        Assert.Equal(new CropNeeds(Raw: 48 + 10 * 160, Packed: 15, Dense: 0), needs);
        Assert.Equal(5, engine.KeptForUpgrades(PackedWheat)); // you can't keep more than you have
        Assert.Equal(0, engine.SellableAmount(PackedWheat));
    }

    [Fact]
    public void Owning_an_upgrade_stops_keeping_crops_for_it()
    {
        var engine = NewEngine();
        Give(engine, Wheat, 100);
        engine.State.Tools["wheat"] = new ToolState { Tier = 1 };

        // The next hoe needs Garden Level 4, so nothing is needed right now.
        Assert.Equal(0, engine.KeptForUpgrades(Wheat));
    }

    // ------------------------------------------------------------------ locks

    [Fact]
    public void Locked_items_are_never_sold()
    {
        var engine = NewEngine();
        engine.State.Settings.KeepForUpgrades = false;
        Give(engine, Wheat, 500);
        Give(engine, Items.Raw(Crops.Carrot), 10);
        engine.SetLocked(Wheat, true);

        Assert.Equal(10 * Crops.Carrot.SellPrice, engine.SellAllCrops());
        Assert.Equal(0, engine.Sell(Wheat.Id, 100));
        Assert.Equal(0, engine.BazaarSell(Wheat.Id, 100, out string reason));
        Assert.Contains("locked", reason);
        Assert.Null(engine.CreateSellOffer(Wheat.Id, 100, 5, out _));
        Assert.Equal(500, engine.GetItem(Wheat));

        engine.SetLocked(Wheat, false);
        Assert.True(engine.Sell(Wheat.Id, 100) > 0);
    }

    // ------------------------------------------------------------------ auto-sell

    [Fact]
    public void Auto_sell_only_sells_the_crops_you_pick()
    {
        var engine = NewEngine(gardenLevel: 2);
        engine.State.Settings.KeepForUpgrades = false;
        engine.SetAutoSell(Crops.Carrot, true);

        var wheat = BreakOne(engine);
        Assert.Equal(0, wheat.CoinsFromAutoSell);
        Assert.Equal(wheat.Drops, engine.GetItem(Wheat));

        engine.PlantField("carrot");
        var carrot = BreakOne(engine);
        Assert.True(carrot.CoinsFromAutoSell > 0);
        Assert.Equal(0, engine.GetItem(Items.Raw(Crops.Carrot)));
    }

    [Fact]
    public void Auto_sell_keeps_what_the_next_upgrades_need_and_never_sells_older_crops()
    {
        var engine = NewEngine();
        engine.SetAutoSell(Crops.Wheat, true);
        Give(engine, Wheat, 40);

        var first = BreakOne(engine); // still short of the 48 Wheat for a hoe: nothing sold
        Assert.Equal(0, first.CoinsFromAutoSell);
        Assert.Equal(40 + first.Drops, engine.GetItem(Wheat));

        Give(engine, Wheat, 100);
        long before = engine.GetItem(Wheat);
        var second = BreakOne(engine, tile: 1);
        Assert.Equal(second.Drops * Crops.Wheat.SellPrice, second.CoinsFromAutoSell); // only the new drops
        Assert.Equal(before, engine.GetItem(Wheat));
    }

    // ------------------------------------------------------------------ the Compactor

    [Fact]
    public void The_compactor_packs_crops_as_they_are_harvested()
    {
        var engine = NewEngine();
        engine.State.Settings.KeepForUpgrades = false;
        engine.State.Compactor = 1;
        Give(engine, Wheat, 159);
        Give(engine, PackedWheat, 159);

        BreakOne(engine);

        Assert.Equal(1 + 159, engine.GetItem(PackedWheat)); // a Compactor doesn't make Dense crops
        Assert.True(engine.GetItem(Wheat) < 160);

        engine.State.Compactor = 2;
        BreakOne(engine, tile: 1);
        Assert.Equal(1, engine.GetItem(DenseWheat));
    }

    [Fact]
    public void The_compactor_leaves_crops_an_upgrade_needs_unpacked()
    {
        var engine = NewEngine(); // the Basic Wheat Sickle needs 48 plain Wheat
        engine.State.Compactor = 1;
        Give(engine, Wheat, 200);

        var first = BreakOne(engine);
        Assert.Equal(0, engine.GetItem(PackedWheat));
        Assert.Equal(200 + first.Drops, engine.GetItem(Wheat));

        Give(engine, Wheat, 10);
        long before = engine.GetItem(Wheat);
        var second = BreakOne(engine, tile: 1);
        Assert.Equal(1, engine.GetItem(PackedWheat));
        Assert.Equal(before + second.Drops - 160, engine.GetItem(Wheat));
        Assert.True(engine.GetItem(Wheat) >= 48);
    }

    [Fact]
    public void The_compactor_can_be_turned_off_for_a_crop()
    {
        var engine = NewEngine();
        engine.State.Settings.KeepForUpgrades = false;
        engine.State.Compactor = 1;
        engine.SetCompacting(Crops.Wheat, false);
        Give(engine, Wheat, 400);

        BreakOne(engine);
        Assert.Equal(0, engine.GetItem(PackedWheat));

        engine.SetCompacting(Crops.Wheat, true); // turning it back on packs straight away
        Assert.Equal(2, engine.GetItem(PackedWheat));
    }

    [Fact]
    public void Crafting_the_compactor_packs_what_you_already_have()
    {
        var engine = NewEngine(gardenLevel: 3, coins: 10_000);
        engine.State.Settings.KeepForUpgrades = false;
        Give(engine, PackedWheat, 8);
        Give(engine, Items.Packed(Crops.Carrot), 8);
        Give(engine, Items.Raw(Crops.Carrot), 500);
        var recipe = engine.NextRecipe(UpgradeKind.Compactor, Compactors.Target)!;
        Assert.Equal("Compactor", recipe.ResultName);

        Assert.True(engine.TryCraft(recipe, out string reason), reason);

        Assert.Equal(1, engine.State.Compactor);
        Assert.Equal(3, engine.GetItem(Items.Packed(Crops.Carrot)));
        Assert.Equal(20, engine.GetItem(Items.Raw(Crops.Carrot)));
        Assert.Equal("Dense Compactor", engine.NextRecipe(UpgradeKind.Compactor, Compactors.Target)!.ResultName);
    }

    // ------------------------------------------------------------------ upgrades: ready & buying what's missing

    [Fact]
    public void Ready_upgrades_are_the_ones_you_can_craft_now()
    {
        var engine = NewEngine(coins: 100);
        Assert.Empty(engine.ReadyRecipes());

        Give(engine, Wheat, 48);
        var ready = Assert.Single(engine.ReadyRecipes());
        Assert.Equal("Basic Wheat Sickle", ready.ResultName);
    }

    [Fact]
    public void Missing_ingredients_can_be_bought_on_the_bazaar()
    {
        var engine = NewEngine(gardenLevel: 4, coins: 100_000);
        engine.State.Tools["wheat"] = new ToolState { Tier = 1 };
        Give(engine, PackedWheat, 3);
        var recipe = engine.NextRecipe(UpgradeKind.Tool, "wheat")!; // 8 Packed Wheat + 5,000 coins

        var missing = Assert.Single(engine.MissingIngredients(recipe));
        Assert.Equal(new Ingredient(PackedWheat.Id, 5), missing);
        long cost = engine.MissingIngredientsCost(recipe);
        Assert.Equal((long)Math.Ceiling(5 * engine.BazaarBuyPrice(PackedWheat)), cost);

        Assert.Equal(cost, engine.BuyMissingIngredients(recipe, out _));
        Assert.Equal(100_000 - cost, engine.State.Coins);
        Assert.Equal(8, engine.GetItem(PackedWheat));
        Assert.True(engine.CanCraft(recipe, out _));
        Assert.Equal(0, engine.BuyMissingIngredients(recipe, out string reason));
        Assert.Equal("You already have everything", reason);
    }

    [Fact]
    public void Buying_missing_ingredients_needs_the_garden_level_and_the_coins()
    {
        var engine = NewEngine(coins: 1_000_000);
        var straw = engine.NextRecipe(UpgradeKind.Armour, nameof(ArmourSlot.Helmet))!; // Garden Level 2
        Assert.Equal(0, engine.BuyMissingIngredients(straw, out string reason));
        Assert.Equal("Needs Garden Level 2", reason);

        engine.State.GardenXp = GardenProgression.TotalXpFor(2);
        engine.State.Coins = 1;
        Assert.Equal(0, engine.BuyMissingIngredients(straw, out reason));
        Assert.Contains("more coins", reason);
        Assert.Equal(1, engine.State.Coins);
    }

    // ------------------------------------------------------------------ which crop pays best

    [Fact]
    public void Coins_per_hour_follow_drops_price_and_speed()
    {
        var engine = NewEngine(gardenLevel: GardenProgression.MaxLevel);
        foreach (var crop in Crops.All)
        {
            double expected = engine.FieldLength / Math.Max(engine.LapSeconds, crop.RegrowSeconds) * 3600
                              * engine.ExpectedDrops(crop, false) * engine.MarketPrice(Items.Raw(crop));
            Assert.Equal(expected, engine.CoinsPerHour(crop), precision: 6);
        }
        Assert.Equal(Crops.All.MaxBy(engine.CoinsPerHour), engine.BestCropForCoins);

        // A good hoe makes its crop pay more.
        double before = engine.CoinsPerHour(Crops.Wheat);
        engine.State.Tools["wheat"] = new ToolState { Tier = 3 };
        Assert.True(engine.CoinsPerHour(Crops.Wheat) > before);
    }

    [Fact]
    public void Best_crop_is_one_you_have_unlocked()
    {
        var engine = NewEngine(gardenLevel: 2);
        Assert.Contains(engine.BestCropForCoins, engine.UnlockedCrops);
    }

    // ------------------------------------------------------------------ pop-ups

    [Fact]
    public void Popups_can_be_hidden_one_kind_at_a_time_or_all_paused()
    {
        var settings = new GameSettings();
        Assert.True(settings.IsPopupShown(PopupKind.LevelUp));

        settings.HiddenPopups.Add(PopupKind.Collection);
        Assert.False(settings.IsPopupShown(PopupKind.Collection));
        Assert.True(settings.IsPopupShown(PopupKind.LevelUp));

        settings.PausePopups = true;
        Assert.False(settings.IsPopupShown(PopupKind.LevelUp));
    }

    // ------------------------------------------------------------------ offline

    [Fact]
    public void Offline_farming_with_auto_sell_and_the_compactor_is_quick()
    {
        var engine = NewEngine(gardenLevel: GardenProgression.MaxLevel);
        engine.State.Compactor = 2;
        engine.SetAutoSell(Crops.Wheat, true);
        engine.State.Field.Length = 24;
        engine.State.Field.Normalize();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = engine.ApplyOfflineProgress(GameEngine.MaxOfflineTime);
        watch.Stop();

        Assert.True(report.Breaks > 10_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed.TotalSeconds:0.0}s");
    }
}
