using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class FarmerAgentTests
{
    private static GameEngine NewEngine() => new(GameState.CreateNew(), new FixedRandom(0.99));

    private static void Grow(GameEngine engine, params int[] tiles)
    {
        foreach (int i in tiles) engine.State.Field.Tiles[i] = engine.FieldCrop.RegrowSeconds;
    }

    /// <summary>Runs only the farmer, so only the crops we grow by hand matter.</summary>
    private static void RunFarmer(FarmerAgent farmer, GameEngine engine, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05) farmer.Tick(0.05, engine);
    }

    private static void RunGame(FarmerAgent farmer, GameEngine engine, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05)
        {
            engine.Tick(0.05);
            farmer.Tick(0.05, engine);
        }
    }

    [Fact]
    public void Farmer_breaks_every_grown_crop_in_one_sweep()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 1, 4, 5, 9);
        var broken = new List<int>();
        farmer.AutoBroke += r => broken.Add(r.TileIndex);

        RunFarmer(farmer, engine, 9 / engine.FarmerSpeed + 0.1);

        Assert.Equal(new[] { 1, 4, 5, 9 }, broken);
        Assert.Equal(0, engine.GrownCount);
    }

    [Fact]
    public void Farmer_turns_around_at_the_end_of_the_row()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 9);
        RunFarmer(farmer, engine, 9 / engine.FarmerSpeed + 0.1);
        Assert.Equal(-1, farmer.Facing);
        Assert.Equal(1, engine.State.Stats.TotalBreaks);

        Grow(engine, 0);
        RunFarmer(farmer, engine, 9 / engine.FarmerSpeed + 0.2);
        Assert.Equal(2, engine.State.Stats.TotalBreaks);
        Assert.Equal(0, farmer.Position, precision: 6);
    }

    [Fact]
    public void Farmer_walks_faster_with_farming_speed()
    {
        var slow = NewEngine();
        var fast = NewEngine();
        fast.State.Talismans[TalismanFamily.Swiftstep] = 3; // +20%
        Grow(slow, 9);
        Grow(fast, 9);
        var slowFarmer = new FarmerAgent(new FixedRandom(0.5));
        var fastFarmer = new FarmerAgent(new FixedRandom(0.5));

        RunFarmer(slowFarmer, slow, 1);
        RunFarmer(fastFarmer, fast, 1);

        Assert.Equal(slowFarmer.Position * 1.2, fastFarmer.Position, precision: 3);
    }

    [Fact]
    public void Farmer_swings_when_breaking_a_crop()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Assert.False(farmer.IsSwinging);

        Grow(engine, 0);
        farmer.Tick(0.05, engine);

        Assert.True(farmer.IsSwinging);
        RunFarmer(farmer, engine, FarmerAgent.SwingDuration + 0.1);
        Assert.False(farmer.IsSwinging);
    }

    [Fact]
    public void Farmer_rests_when_nothing_is_grown()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 3);
        RunFarmer(farmer, engine, 3);
        Assert.False(farmer.IsFarming);
        Assert.Equal(1, engine.State.Stats.TotalBreaks);
    }

    [Fact]
    public void Farmer_leaves_crops_alone_when_auto_harvest_is_off()
    {
        var engine = NewEngine();
        engine.State.Settings.FarmerAutoHarvest = false;
        var farmer = new FarmerAgent();

        RunGame(farmer, engine, 20);

        Assert.Equal(engine.FieldLength, engine.GrownCount);
        Assert.Equal(0, engine.State.Stats.TotalBreaks);
        Assert.False(farmer.IsFarming);
    }

    [Fact]
    public void Farmer_keeps_up_with_the_regrowing_row()
    {
        var engine = new GameEngine(GameState.CreateNew());
        var farmer = new FarmerAgent();

        RunGame(farmer, engine, 5 * 60);

        // 10 wheat regrowing every 5 s is at most 2 breaks a second; walking makes it a bit less.
        Assert.InRange(engine.State.Stats.TotalBreaks, 400, 600);
    }
}
