using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>Visitors, the Visitor Shop, timed boosts and the Composter.</summary>
public class GardenTests
{
    private static GameEngine NewEngine(int gardenLevel, int seed = 7)
    {
        var engine = new GameEngine(GameState.CreateNew(), new Random(seed));
        engine.State.GardenXp = GardenProgression.TotalXpFor(gardenLevel);
        return engine;
    }

    private static void Give(GameEngine engine, ItemDef item, long count) =>
        engine.State.Inventory[item.Id] = engine.GetItem(item) + count;

    private static void GiveWhatTheyWant(GameEngine engine, VisitorRequest request)
    {
        foreach (var want in request.Wants) Give(engine, want.Item, want.Count);
    }

    // ------------------------------------------------------------------ visitors

    [Fact]
    public void Visitors_start_coming_at_garden_level_2_and_fill_the_seats()
    {
        var early = NewEngine(gardenLevel: 1);
        early.Tick(3_600);
        Assert.Empty(early.Visitors);

        var engine = NewEngine(gardenLevel: 2);
        int arrived = 0;
        engine.VisitorArrived += _ => arrived++;
        engine.Tick(Garden.FirstVisitorSeconds);
        Assert.Single(engine.Visitors);

        engine.Tick(Garden.VisitorIntervalSeconds * 10);
        Assert.Equal(Garden.BaseSeats, engine.Visitors.Count); // no more than there are seats
        Assert.Equal(Garden.BaseSeats, arrived);
        Assert.Equal(engine.Visitors.Count, engine.Visitors.Select(v => v.VisitorId).Distinct().Count()); // no one twice
    }

    [Fact]
    public void Visitors_ask_for_unlocked_crops_and_pay_more_than_the_market()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var engine = NewEngine(gardenLevel: 8, seed);
            var request = engine.NewVisitorRequest();
            Assert.NotEmpty(request.Wants);
            Assert.All(request.Wants, w => Assert.True(engine.IsCropUnlocked(w.Item.Crop), $"{w.Item.Name} isn't unlocked"));
            Assert.True(request.Visitor.GardenLevel <= 8);

            double worth = request.Wants.Sum(w => w.Count * w.Item.SellPrice);
            Assert.Equal((long)Math.Round(worth * Garden.CoinBonus), request.Coins);
            Assert.True(request.Tokens >= 2);
            Assert.True(request.GardenXp > 0 && request.FarmingXp > 0);
        }
    }

    [Fact]
    public void Serving_a_visitor_trades_their_crops_for_the_reward()
    {
        var engine = NewEngine(gardenLevel: 2);
        engine.Tick(Garden.FirstVisitorSeconds);
        var request = engine.Visitors[0];

        Assert.False(engine.ServeVisitor(request.Id, out string reason));
        Assert.StartsWith("Needs", reason);

        GiveWhatTheyWant(engine, request);
        long gardenXp = engine.State.GardenXp;
        int levelBefore = engine.FarmingLevel;
        Assert.True(engine.ServeVisitor(request.Id, out _));
        Assert.Empty(engine.Visitors);

        // The reward, plus the coins for any Farming levels its XP gave.
        long levelCoins = Enumerable.Range(levelBefore + 1, engine.FarmingLevel - levelBefore)
            .Sum(level => (long)level * FarmingSkill.CoinRewardPerLevel);
        Assert.Equal(request.Coins + levelCoins, engine.State.Coins);
        Assert.Equal(request.Tokens, engine.Tokens);
        Assert.Equal(gardenXp + request.GardenXp, engine.State.GardenXp);
        Assert.All(request.Wants, w => Assert.Equal(0, engine.GetItem(w.Item)));
        Assert.Equal(1, engine.State.Garden.VisitorsServed);
    }

    [Fact]
    public void Locked_crops_are_not_given_away_and_visitors_can_be_sent_off()
    {
        var engine = NewEngine(gardenLevel: 2);
        engine.Tick(Garden.FirstVisitorSeconds);
        var request = engine.Visitors[0];
        GiveWhatTheyWant(engine, request);
        engine.SetLocked(request.Wants[0].Item, true);

        Assert.False(engine.ServeVisitor(request.Id, out string reason));
        Assert.Contains("locked", reason);

        Assert.True(engine.RefuseVisitor(request.Id));
        Assert.Empty(engine.Visitors);
        Assert.Equal(0, engine.State.Coins);
    }

    [Fact]
    public void Visitors_arrive_while_the_game_is_closed()
    {
        var engine = NewEngine(gardenLevel: 4);
        var report = engine.ApplyOfflineProgress(TimeSpan.FromHours(2));
        Assert.Equal(Garden.BaseSeats, report.VisitorsArrived);
        Assert.Equal(Garden.BaseSeats, engine.Visitors.Count);
    }

    // ------------------------------------------------------------------ the Visitor Shop & boosts

    [Fact]
    public void Tokens_buy_boosts_that_run_out()
    {
        var engine = NewEngine(gardenLevel: 2);
        engine.State.Garden.Tokens = 20;
        int fortune = engine.GetFortune(Crops.Wheat, false);
        var seeds = Garden.Shop.First(i => i.Id == "lucky_seeds");

        Assert.True(engine.Buy(seeds, out _));
        Assert.Equal(20 - seeds.Cost, engine.Tokens);
        Assert.Equal(fortune + 40, engine.GetFortune(Crops.Wheat, false));
        Assert.Contains(engine.FortuneSources(Crops.Wheat), s => s.Source.StartsWith("Lucky Seeds"));

        Assert.True(engine.Buy(seeds, out _)); // buying again adds time
        Assert.Equal(60 * 60, engine.BoostSecondsLeft("lucky_seeds"), precision: 3);

        engine.Tick(60 * 60);
        Assert.Equal(fortune, engine.GetFortune(Crops.Wheat, false));
        Assert.Empty(engine.ActiveBoosts);
    }

    [Fact]
    public void Benches_add_seats_and_get_dearer()
    {
        var engine = NewEngine(gardenLevel: 2);
        engine.State.Garden.Tokens = 1_000;
        var bench = Garden.Shop.First(i => i.Kind == ShopKind.Seat);

        Assert.Equal(15, engine.ShopPrice(bench));
        Assert.True(engine.Buy(bench, out _));
        Assert.Equal(30, engine.ShopPrice(bench));
        Assert.True(engine.Buy(bench, out _));
        Assert.True(engine.Buy(bench, out _));
        Assert.Equal(Garden.BaseSeats + Garden.MaxExtraSeats, engine.VisitorSeats);
        Assert.False(engine.Buy(bench, out string reason));
        Assert.Equal("All the benches are out", reason);
        Assert.Equal(1_000 - 15 - 30 - 60, engine.Tokens);
    }

    [Fact]
    public void Shop_cosmetics_unlock_in_the_wardrobe()
    {
        var engine = NewEngine(gardenLevel: 2);
        var hood = Cosmetics.Find("hood")!;
        Assert.False(hood.IsUnlocked(engine));

        engine.State.Garden.Tokens = 100;
        Assert.True(engine.Buy(Garden.Shop.First(i => i.Target == "hood"), out _));
        Assert.True(hood.IsUnlocked(engine));
        Assert.False(engine.CanBuy(Garden.Shop.First(i => i.Target == "hood"), out string reason));
        Assert.Equal("Already yours", reason);
    }

    [Fact]
    public void Offline_farming_only_gets_a_boost_while_it_lasts()
    {
        // Same farm, same luck: one with a 30-minute Swift Tonic, one without, away for 4 hours.
        GameEngine Farm(bool boosted)
        {
            var engine = NewEngine(gardenLevel: 2, seed: 1);
            engine.State.Settings.KeepForUpgrades = false;
            if (boosted)
            {
                engine.State.Garden.Tokens = 6;
                Assert.True(engine.Buy(Garden.Shop.First(i => i.Id == "swift_tonic"), out _));
            }
            return engine;
        }
        var plain = Farm(false);
        var boosted = Farm(true);
        long plainBreaks = plain.ApplyOfflineProgress(TimeSpan.FromHours(4)).Breaks;
        long boostedBreaks = boosted.ApplyOfflineProgress(TimeSpan.FromHours(4)).Breaks;

        Assert.True(boostedBreaks > plainBreaks);
        Assert.True(boostedBreaks < plainBreaks * 1.05, "the boost should only cover its 30 minutes, not all 4 hours");
        Assert.Empty(boosted.ActiveBoosts);
    }

    // ------------------------------------------------------------------ the Composter

    [Fact]
    public void Crops_in_the_composter_become_compost_over_time()
    {
        var engine = NewEngine(gardenLevel: 3);
        Give(engine, Items.Raw(Crops.Wheat), 5_000); // worth 10,000 matter: 2 Compost and some left

        Assert.Equal(5_000, engine.AddToComposter("wheat", 5_000, out _));
        Assert.Equal(10_000, engine.Matter);
        engine.Tick(Garden.SecondsPerCompost - 1);
        Assert.Equal(0, engine.Compost);
        engine.Tick(Garden.SecondsPerCompost * 5);
        Assert.Equal(2, engine.Compost);
        Assert.Equal(2_000, engine.Matter);
    }

    [Fact]
    public void Composter_needs_garden_level_3_and_skips_locked_and_kept_crops()
    {
        var early = NewEngine(gardenLevel: 2);
        Give(early, Items.Raw(Crops.Wheat), 100);
        Assert.Equal(0, early.AddToComposter("wheat", 100, out string reason));
        Assert.Equal("Needs Garden Level 3", reason);

        var engine = NewEngine(gardenLevel: 3);
        Give(engine, Items.Raw(Crops.Wheat), 1_000);
        Give(engine, Items.Raw(Crops.Carrot), 500);
        engine.SetLocked(Items.Raw(Crops.Carrot), true);
        long kept = engine.KeptForUpgrades(Items.Raw(Crops.Wheat));
        Assert.True(kept > 0);

        Assert.Equal(1_000 - kept, engine.AddSpareCropsToComposter());
        Assert.Equal(kept, engine.GetItem(Items.Raw(Crops.Wheat)));
        Assert.Equal(500, engine.GetItem(Items.Raw(Crops.Carrot)));
    }

    [Fact]
    public void Compost_buys_permanent_upgrades_and_fertiliser()
    {
        var engine = NewEngine(gardenLevel: 3);
        engine.State.Garden.Compost = 20;
        var soil = Garden.FindUpgrade("rich_soil")!;
        var steps = Garden.FindUpgrade("quick_steps")!;
        int fortune = engine.GetFortune(Crops.Wheat, false);
        double speed = engine.FarmerSpeed;

        Assert.True(engine.BuyUpgrade(soil, out _));  // 3 Compost
        Assert.True(engine.BuyUpgrade(soil, out _));  // 6 Compost
        Assert.True(engine.BuyUpgrade(steps, out _)); // 5 Compost
        Assert.Equal(20 - 3 - 6 - 5, engine.Compost);
        Assert.Equal(fortune + 8, engine.GetFortune(Crops.Wheat, false));
        Assert.True(engine.FarmerSpeed > speed);
        Assert.False(engine.BuyUpgrade(soil, out string reason)); // the 3rd level costs 9
        Assert.Equal("Needs 3 more Compost", reason);

        Assert.True(engine.Fertilise(out _)); // 6 Compost left: enough for two
        Assert.Equal(fortune + 8 + 30, engine.GetFortune(Crops.Wheat, false));
        Assert.Equal(20 * 60, engine.BoostSecondsLeft("fertiliser"), precision: 3);
        Assert.True(engine.Fertilise(out _)); // more time, not more Fortune
        Assert.Equal(fortune + 8 + 30, engine.GetFortune(Crops.Wheat, false));
        Assert.Equal(40 * 60, engine.BoostSecondsLeft("fertiliser"), precision: 3);
        Assert.False(engine.Fertilise(out reason));
        Assert.Equal("Needs 3 more Compost", reason);
    }

    [Fact]
    public void Busy_composter_makes_compost_faster()
    {
        var engine = NewEngine(gardenLevel: 3);
        engine.State.Garden.Upgrades["busy_composter"] = 5;
        Assert.Equal(Garden.SecondsPerCompost / 1.5, engine.SecondsPerCompost, precision: 6);
    }

    [Fact]
    public void Garden_progress_survives_saving_and_reset_clears_it()
    {
        string path = Path.Combine(Path.GetTempPath(), "tbg-garden-" + Guid.NewGuid().ToString("N"), "save.json");
        try
        {
            var engine = NewEngine(gardenLevel: 4);
            engine.Tick(Garden.FirstVisitorSeconds);
            engine.State.Garden.Tokens = 12;
            engine.State.Garden.Compost = 3;
            engine.State.Garden.Matter = 1_234;
            engine.State.Garden.Upgrades["rich_soil"] = 2;
            engine.State.Garden.Boosts["lucky_seeds"] = 600;
            var saves = new SaveManager(path);
            saves.Save(engine.State);

            var loaded = new GameEngine(saves.LoadOrCreate());
            Assert.Single(loaded.Visitors);
            Assert.Equal(engine.Visitors[0].Wants, loaded.Visitors[0].Wants);
            Assert.Equal(12, loaded.Tokens);
            Assert.Equal(3, loaded.Compost);
            Assert.Equal(1_234, loaded.Matter);
            Assert.Equal(2, loaded.UpgradeLevel(Garden.FindUpgrade("rich_soil")!));
            Assert.Equal(600, loaded.BoostSecondsLeft("lucky_seeds"));

            loaded.ResetProgress();
            Assert.Empty(loaded.Visitors);
            Assert.Equal(0, loaded.Tokens);
            Assert.Empty(loaded.ActiveBoosts);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
