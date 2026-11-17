using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class FarmerAgentTests
{
    private static GameEngine NewEngine(int plots = 3)
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.99));
        engine.State.GardenXp = GardenProgression.TotalXpFor(GardenProgression.MaxLevel);
        engine.State.Coins = long.MaxValue / 2;
        while (engine.Plots.Count < plots && engine.TryUnlockNextPlot(out _)) { }
        engine.State.Coins = 0;
        return engine;
    }

    private static void Ripen(GameEngine engine, params int[] plots)
    {
        foreach (int i in plots) engine.Plots[i].Growth = Crops.Wheat.GrowSeconds;
    }

    /// <summary>Runs the farmer without growing crops, so only the crops we ripen matter.</summary>
    private static void RunFarmerOnly(FarmerAgent farmer, GameEngine engine, double seconds)
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
    public void Farmer_breaks_every_ripe_crop_in_one_sweep_without_stopping()
    {
        var engine = NewEngine(plots: 6);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Ripen(engine, 1, 3, 5);
        var harvested = new List<int>();
        farmer.AutoHarvested += r => harvested.Add(r.PlotIndex);

        // One pass from plot 0 to plot 5 takes 5 / SweepSpeed seconds.
        RunFarmerOnly(farmer, engine, 5 / FarmerAgent.SweepSpeed + 0.1);

        Assert.Equal(new[] { 1, 3, 5 }, harvested);
        Assert.All(engine.Plots, p => Assert.False(GameEngine.IsRipe(p)));
        Assert.True(engine.Plots.All(p => p.Growth == 0)); // replanted
    }

    [Fact]
    public void Farmer_turns_around_at_the_end_of_the_row()
    {
        var engine = NewEngine(plots: 4);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Ripen(engine, 3);
        RunFarmerOnly(farmer, engine, 0.2);
        Assert.True(farmer.IsFarming);
        Assert.Equal(1, farmer.Facing);

        // Reaches the end, breaks plot 3, and is now facing back the way they came.
        RunFarmerOnly(farmer, engine, 3 / FarmerAgent.SweepSpeed);
        Assert.Equal(-1, farmer.Facing);
        Assert.Equal(1, engine.State.Stats.TotalHarvests);

        // A crop behind them gets picked up on the way back.
        Ripen(engine, 0);
        RunFarmerOnly(farmer, engine, 3 / FarmerAgent.SweepSpeed + 0.2);
        Assert.Equal(2, engine.State.Stats.TotalHarvests);
        Assert.Equal(0, farmer.Position, precision: 6);
    }

    [Fact]
    public void Farmer_heads_toward_the_nearest_ripe_crop_when_starting()
    {
        var engine = NewEngine(plots: 6);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        RunFarmerOnly(farmer, engine, 0.1);
        Assert.False(farmer.IsFarming);

        Ripen(engine, 5);
        RunFarmerOnly(farmer, engine, 0.1);

        Assert.True(farmer.IsFarming);
        Assert.Equal(FarmerAction.Walking, farmer.Action);
        Assert.Equal(1, farmer.Facing);
    }

    [Fact]
    public void Farmer_swings_after_breaking_a_crop()
    {
        var engine = NewEngine(plots: 3);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Assert.False(farmer.IsSwinging);

        Ripen(engine, 0);
        farmer.Tick(0.05, engine); // standing on plot 0 already

        Assert.True(farmer.IsSwinging);
        RunFarmerOnly(farmer, engine, FarmerAgent.SwingDuration + 0.1);
        Assert.False(farmer.IsSwinging);
    }

    [Fact]
    public void Farmer_stops_farming_when_nothing_is_ripe()
    {
        var engine = NewEngine(plots: 6);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Ripen(engine, 2);

        RunFarmerOnly(farmer, engine, 2);

        Assert.False(farmer.IsFarming);
        Assert.Equal(1, engine.State.Stats.TotalHarvests);
    }

    [Fact]
    public void Clicking_a_crop_first_doesnt_confuse_the_farmer()
    {
        var engine = NewEngine(plots: 6);
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Ripen(engine, 4);
        farmer.Tick(0.1, engine);

        engine.Harvest(4, handHarvest: true);
        RunFarmerOnly(farmer, engine, 3);

        Assert.Equal(1, engine.State.Stats.TotalHarvests);
        Assert.Equal(1, engine.State.Stats.HandHarvests);
        Assert.False(farmer.IsFarming);
    }

    [Fact]
    public void Farmer_leaves_crops_alone_when_auto_harvest_is_off()
    {
        var engine = NewEngine();
        engine.State.Settings.FarmerAutoHarvest = false;
        var farmer = new FarmerAgent();
        engine.Tick(1000);

        RunGame(farmer, engine, 10);

        Assert.All(engine.Plots, p => Assert.True(GameEngine.IsRipe(p)));
        Assert.Equal(0, engine.State.Stats.TotalHarvests);
        Assert.False(farmer.IsFarming);
    }

    [Fact]
    public void Farmer_keeps_farming_for_a_long_time()
    {
        var engine = new GameEngine(GameState.CreateNew());
        var farmer = new FarmerAgent();

        RunGame(farmer, engine, 10 * 60);

        // 3 wheat plots for 10 minutes: close to 2 harvests a minute each, minus walking.
        Assert.InRange(engine.State.Stats.TotalHarvests, 50, 60);
        Assert.InRange(farmer.Position, 0, 2);
    }
}
