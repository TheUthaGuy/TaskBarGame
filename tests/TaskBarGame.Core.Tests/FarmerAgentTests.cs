using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class FarmerAgentTests
{
    private static GameEngine NewEngine() => new(GameState.CreateNew(), new FixedRandom(0.5));

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
    public void Farmer_walks_the_row_one_way_breaking_every_grown_crop()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 1, 4, 5, 9);
        var broken = new List<int>();
        farmer.AutoBroke += r => broken.Add(r.TileIndex);

        RunFarmer(farmer, engine, 9 / engine.FarmerSpeed + 0.1);

        Assert.Equal(new[] { 1, 4, 5, 9 }, broken);
        Assert.Equal(0, engine.GrownCount);
        Assert.Equal(1, farmer.Facing);
    }

    [Fact]
    public void Farmer_teleports_from_the_end_pad_back_to_the_start()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 9);
        int teleports = 0;
        farmer.Teleported += () => teleports++;

        // Walk onto the end pad (one step past the last crop).
        RunFarmer(farmer, engine, engine.FieldLength / engine.FarmerSpeed + 0.1);
        Assert.Equal(1, teleports);
        Assert.True(farmer.IsTeleporting);

        // A crop behind them is picked up on the next lap, never by walking backwards.
        Grow(engine, 0);
        RunFarmer(farmer, engine, FarmerAgent.TeleportDuration);
        Assert.False(farmer.IsTeleporting);
        Assert.InRange(farmer.Position, FarmerAgent.StartPad, 0.5);

        RunFarmer(farmer, engine, 1.5 / engine.FarmerSpeed);
        Assert.Equal(2, engine.State.Stats.TotalBreaks);
        Assert.Equal(1, farmer.Facing);
    }

    [Fact]
    public void Teleport_fades_out_then_appears_at_the_start()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 9);
        RunFarmer(farmer, engine, engine.FieldLength / engine.FarmerSpeed + 0.06);
        Assert.True(farmer.IsTeleporting);
        Assert.Equal(engine.FieldLength, farmer.Position, precision: 6);

        Grow(engine, 3); // keep them busy so they don't wander off afterwards
        farmer.Tick(FarmerAgent.TeleportDuration * 0.6, engine);
        Assert.True(farmer.TeleportProgress > 0.5);
        Assert.Equal(FarmerAgent.StartPad, farmer.Position, precision: 6);
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
    public void Farmer_finishes_the_lap_then_rests_when_nothing_has_grown()
    {
        var engine = NewEngine();
        var farmer = new FarmerAgent(new FixedRandom(0.5));
        Grow(engine, 3);

        RunFarmer(farmer, engine, 2);
        Assert.True(farmer.IsFarming); // the row is broken but the lap isn't over

        RunFarmer(farmer, engine, engine.LapSeconds);
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
    public void Every_crop_is_grown_again_by_the_time_the_farmer_comes_round()
    {
        foreach (var crop in Crops.All)
        {
            var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.5));
            engine.State.GardenXp = GardenProgression.TotalXpFor(crop.UnlockGardenLevel);
            engine.State.Coins = long.MaxValue / 2;
            while (engine.TryExpandField(out _)) { } // as long as the row can be by now
            engine.State.Talismans[TalismanFamily.Swiftstep] = 3;
            foreach (var slot in Armour.Slots) engine.State.Armour[slot] = 2; // fastest farmer
            engine.PlantField(crop.Id);
            var farmer = new FarmerAgent(new FixedRandom(0.5));
            RunGame(farmer, engine, 30);

            long before = engine.State.Stats.TotalBreaks;
            RunGame(farmer, engine, engine.LapSeconds * 4);
            long laps = engine.State.Stats.TotalBreaks - before;

            // Four laps should break every crop four times: no gaps in the row.
            Assert.True(laps >= 4 * engine.FieldLength - 2, $"{crop.Name}: only {laps} crops in 4 laps of {engine.FieldLength}");
        }
    }

    [Fact]
    public void Farmer_keeps_farming_for_a_long_time()
    {
        var engine = new GameEngine(GameState.CreateNew());
        var farmer = new FarmerAgent();

        RunGame(farmer, engine, 5 * 60);

        // A 10-crop row, one lap every LapSeconds.
        double expected = 300 / engine.LapSeconds * engine.FieldLength;
        Assert.InRange(engine.State.Stats.TotalBreaks, expected * 0.9, expected * 1.05);
    }
}
