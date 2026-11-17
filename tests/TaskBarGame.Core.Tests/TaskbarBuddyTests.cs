using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

/// <summary>Farmer cosmetics, the per-hour rates and save backups.</summary>
public sealed class TaskbarBuddyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tbg-buddy-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_dir, "save.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static GameEngine NewEngine() => new(GameState.CreateNew(), new FixedRandom(0.5));

    // ------------------------------------------------------------------ cosmetics

    [Fact]
    public void Everyone_starts_with_the_straw_hat_and_classic_overalls()
    {
        var engine = NewEngine();
        Assert.Equal("straw", Cosmetics.WornHat(engine).Id);
        Assert.Equal("classic", Cosmetics.WornOutfit(engine).Id);
        Assert.Equal(new[] { "straw", "classic" }, Cosmetics.All.Where(c => c.IsUnlocked(engine)).Select(c => c.Id));
        Assert.Empty(Cosmetics.TakeNewlyUnlocked(engine)); // the starting ones aren't announced
        Assert.Equal(Cosmetics.All.Count(), Cosmetics.All.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Milestones_unlock_cosmetics_and_each_is_announced_once()
    {
        var engine = NewEngine();
        engine.State.FarmingXp = FarmingSkill.TotalXpFor(10);
        engine.State.Stats.HandBreaks = 1_000;

        var fresh = Cosmetics.TakeNewlyUnlocked(engine);
        Assert.Equal(new[] { "cap", "meadow" }, fresh.Select(c => c.Id));
        Assert.Empty(Cosmetics.TakeNewlyUnlocked(engine));

        var bandana = Cosmetics.Find("bandana")!;
        engine.State.Stats.TotalBreaks = 7_420;
        Assert.Equal((7_420L, 10_000L), bandana.Progress(engine));
        Assert.False(bandana.IsUnlocked(engine));
    }

    [Fact]
    public void A_locked_choice_falls_back_to_the_starting_look()
    {
        var engine = NewEngine();
        engine.State.Settings.Hat = "crown";
        engine.State.Settings.Outfit = "golden";
        Assert.Equal("straw", Cosmetics.WornHat(engine).Id);
        Assert.Equal("classic", Cosmetics.WornOutfit(engine).Id);

        foreach (var slot in Armour.Slots) engine.State.Armour[slot] = Armour.MaxTier;
        Assert.Equal("golden", Cosmetics.WornOutfit(engine).Id);

        engine.State.Settings.Hat = "no such hat";
        engine.State.Settings.Normalize();
        Assert.Equal(Cosmetics.DefaultHat, engine.State.Settings.Hat);
    }

    [Fact]
    public void Flower_crown_needs_a_maxed_collection()
    {
        var engine = NewEngine();
        var crown = Cosmetics.Find("flowers")!;
        engine.State.Collections["melon"] = CollectionTiers.MaxThreshold(Crops.Melon) - 1;
        Assert.False(crown.IsUnlocked(engine));
        engine.State.Collections["melon"]++;
        Assert.True(crown.IsUnlocked(engine));
    }

    // ------------------------------------------------------------------ per-hour rates

    [Fact]
    public void Rates_are_per_hour_over_the_recent_window_and_the_session()
    {
        var tracker = new RateTracker(windowSeconds: 600);
        tracker.Start(0);
        for (int t = 0; t < 1_200; t++)
            tracker.Add(t, coins: t < 600 ? 1 : 2, crops: 1, xp: 0.5);

        var recent = tracker.Recent(1_200);
        Assert.Equal(7_200, recent.CoinsPerHour, precision: 0);  // 2 coins a second for the last 10 minutes
        Assert.Equal(3_600, recent.CropsPerHour, precision: 0);
        Assert.Equal(1_800, recent.XpPerHour, precision: 0);
        Assert.True(recent.IsMeasured);

        var session = tracker.Session(1_200);
        Assert.Equal(5_400, session.CoinsPerHour, precision: 0); // 1.5 a second on average
    }

    [Fact]
    public void Rates_need_a_minute_before_they_mean_much()
    {
        var tracker = new RateTracker();
        Assert.False(tracker.Recent(0).IsMeasured);
        tracker.Start(100);
        tracker.Add(110, 50, 10, 5);
        Assert.False(tracker.Recent(130).IsMeasured);
        Assert.True(tracker.Recent(170).IsMeasured);
        Assert.Equal(50 * 3600 / 70.0, tracker.Recent(170).CoinsPerHour, precision: 6);
    }

    // ------------------------------------------------------------------ backups, export & import

    [Fact]
    public void Backups_are_listed_newest_first_and_only_the_last_ten_are_kept()
    {
        var saves = new SaveManager(SavePath);
        var state = GameState.CreateNew();
        var start = new DateTime(2026, 11, 1, 9, 0, 0);
        for (int i = 0; i < 12; i++)
        {
            state.Coins = i;
            saves.CreateBackup(state, now: start.AddHours(i));
        }

        var backups = saves.ListBackups();
        Assert.Equal(SaveManager.BackupsKept, backups.Count);
        Assert.Equal(start.AddHours(11), backups[0].Time);
        Assert.Equal(start.AddHours(2), backups[^1].Time);
        Assert.Contains("11 coins", backups[0].Summary);
        Assert.Equal(11, SaveManager.Read(backups[0].Path)!.Coins);
    }

    [Fact]
    public void Exported_saves_can_be_read_back_but_other_files_cannot()
    {
        var saves = new SaveManager(SavePath);
        Directory.CreateDirectory(_dir);
        var state = GameState.CreateNew();
        state.Coins = 4321;
        string exported = Path.Combine(_dir, "my farm.json");
        saves.Export(state, exported);
        Assert.Equal(4321, SaveManager.Read(exported)!.Coins);

        string other = Path.Combine(_dir, "other.json");
        File.WriteAllText(other, "{}");
        Assert.Null(SaveManager.Read(other));
        File.WriteAllText(other, """{ "Version": 1, "Coins": 5, "Plots": [] }"""); // the original edition's save
        Assert.Null(SaveManager.Read(other));
        File.WriteAllText(other, "not json");
        Assert.Null(SaveManager.Read(other));
        Assert.Null(SaveManager.Read(Path.Combine(_dir, "missing.json")));
    }
}
