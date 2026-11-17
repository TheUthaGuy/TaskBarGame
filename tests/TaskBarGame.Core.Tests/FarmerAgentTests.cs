using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class FarmerAgentTests
{
    private static void Run(FarmerAgent farmer, GameEngine engine, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.05)
        {
            engine.Tick(0.05);
            farmer.Tick(0.05, engine);
        }
    }

    [Fact]
    public void Farmer_walks_to_ripe_crops_and_harvests_them()
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.99));
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        engine.Plots[2].Growth = Crops.Wheat.GrowSeconds;
        var harvested = new List<HarvestResult>();
        farmer.AutoHarvested += harvested.Add;

        Run(farmer, engine, 3);

        var result = Assert.Single(harvested);
        Assert.Equal(2, result.PlotIndex);
        Assert.False(result.HandHarvest);
        Assert.Equal(2, farmer.Position, precision: 3);
        Assert.Equal(1, farmer.Facing);
        Assert.False(GameEngine.IsRipe(engine.Plots[2]));
    }

    [Fact]
    public void Farmer_gives_up_on_crops_you_harvest_first()
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.99));
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        engine.Plots[2].Growth = Crops.Wheat.GrowSeconds;

        farmer.Tick(0.1, engine);
        Assert.Equal(2, farmer.TargetPlot);
        Assert.Equal(FarmerAction.Walking, farmer.Action);

        engine.Harvest(2, handHarvest: true);
        farmer.Tick(0.1, engine);

        Assert.Null(farmer.TargetPlot);
    }

    [Fact]
    public void Farmer_leaves_crops_alone_when_auto_harvest_is_off()
    {
        var engine = new GameEngine(GameState.CreateNew());
        engine.State.Settings.FarmerAutoHarvest = false;
        var farmer = new FarmerAgent();
        engine.Tick(1000);

        Run(farmer, engine, 10);

        Assert.All(engine.Plots, p => Assert.True(GameEngine.IsRipe(p)));
        Assert.Equal(0, engine.State.Stats.TotalHarvests);
    }

    [Fact]
    public void Farmer_keeps_farming_for_a_long_time()
    {
        var engine = new GameEngine(GameState.CreateNew());
        var farmer = new FarmerAgent();

        Run(farmer, engine, 10 * 60);

        // 3 wheat plots for 10 minutes: close to 2 harvests a minute each, minus walking.
        Assert.InRange(engine.State.Stats.TotalHarvests, 45, 60);
        Assert.InRange(farmer.Position, 0, 2);
    }
}
