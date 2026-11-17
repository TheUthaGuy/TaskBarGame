using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class RareFindTests
{
    private static GameEngine Engine(double roll)
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(roll));
        engine.Tick(1_000); // grow the row
        return engine;
    }

    [Fact]
    public void Rare_finds_go_from_most_common_to_rarest_and_are_tiny_chances()
    {
        var all = RareFinds.All;
        for (int i = 1; i < all.Count; i++)
        {
            Assert.True(all[i].OneIn > all[i - 1].OneIn, "each find should be rarer than the one before");
            Assert.True(all[i].SellPrice > all[i - 1].SellPrice, "rarer finds should be worth more");
        }
        Assert.True(all.Sum(f => 1.0 / f.OneIn) < 0.001, "together, well under 1 in 1,000 crops");
        Assert.All(all, f => Assert.False(f.Bonus.IsEmpty));
    }

    [Fact]
    public void Most_crops_find_nothing()
    {
        var engine = Engine(0.99);
        for (int i = 0; i < engine.FieldLength; i++) engine.Break(i, handBreak: false);
        Assert.Equal(0, engine.RareFindsInAlbum);
    }

    [Fact]
    public void A_lucky_roll_finds_the_rarest_and_adds_it_to_the_album()
    {
        var engine = Engine(0.0); // the luckiest roll there is
        RareDrop? drop = null;
        engine.RareFound += d => drop = d;
        double speed = engine.FarmerSpeed;

        engine.Break(3, handBreak: true);

        Assert.NotNull(drop);
        Assert.Equal("acorn", drop!.Find.Id);
        Assert.True(drop.First);
        Assert.Equal(3, drop.TileIndex);
        Assert.Equal(1, engine.RareFindsInAlbum);
        Assert.True(engine.FarmerSpeed > speed); // Crystal Acorn: +5% speed, +5% XP
        Assert.Equal(5, engine.XpPercent);
    }

    [Fact]
    public void A_roll_just_past_the_rarest_finds_the_next_one()
    {
        var engine = Engine(1.0 / 400_000 + 1e-9);
        engine.Break(0, handBreak: false);
        var star = RareFinds.Find("star")!;
        Assert.Equal(1, engine.RareFind(star).Found);
        Assert.Contains(engine.FortuneSources(Crops.Wheat), s => s.Source == "Star Fragment (rare find)" && s.Amount == 10);
    }

    [Fact]
    public void Extra_finds_can_be_sold_but_the_bonus_counts_once()
    {
        var engine = Engine(0.0);
        var drops = new List<RareDrop>();
        engine.RareFound += drops.Add;
        engine.Break(0, handBreak: false);
        engine.Break(1, handBreak: false);
        Assert.True(drops[0].First);
        Assert.False(drops[1].First);

        var acorn = RareFinds.Find("acorn")!;
        Assert.Equal(5, engine.XpPercent); // not 10
        long coins = engine.State.Coins;
        Assert.Equal((long)Math.Floor(engine.RareFindPrice(acorn)), engine.SellRareFind("acorn", 1));
        Assert.Equal(coins + (long)Math.Floor(engine.RareFindPrice(acorn)), engine.State.Coins);
        Assert.Equal(2, engine.RareFind(acorn).Found);
        Assert.Equal(1, engine.RareFind(acorn).Held);
        Assert.Equal(5, engine.XpPercent); // selling doesn't take the album bonus away
        Assert.Equal(0, engine.SellRareFind("acorn", 0));
        Assert.True(engine.SellRareFind("acorn", 99) > 0);
        Assert.Equal(0, engine.SellRareFind("acorn", 1)); // none left
    }

    [Fact]
    public void Finds_while_the_game_is_closed_are_counted()
    {
        var engine = Engine(0.0);
        var report = engine.ApplyOfflineProgress(TimeSpan.FromMinutes(10));
        Assert.True(report.Breaks > 0);
        Assert.Equal(report.Breaks, report.RareFinds);
    }

    [Fact]
    public void Rare_finds_are_saved_and_reset_clears_them()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tbg-rare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var engine = Engine(0.0);
            engine.Break(0, handBreak: false);
            var saves = new SaveManager(Path.Combine(dir, "save.json"));
            saves.Save(engine.State);

            var loaded = new GameEngine(saves.LoadOrCreate());
            Assert.Equal(1, loaded.RareFind(RareFinds.Find("acorn")!).Held);
            loaded.ResetProgress();
            Assert.Equal(0, loaded.RareFindsInAlbum);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
