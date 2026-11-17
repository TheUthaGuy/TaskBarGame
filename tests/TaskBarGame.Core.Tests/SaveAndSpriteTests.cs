using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public sealed class SaveManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tbg-tests-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_dir, "save.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_save_starts_a_new_game()
    {
        var state = new SaveManager(SavePath).LoadOrCreate();
        Assert.Equal(3, state.Plots.Count);
    }

    [Fact]
    public void Save_and_load_round_trips()
    {
        var saves = new SaveManager(SavePath);
        var state = GameState.CreateNew();
        state.Coins = 1234;
        state.FarmingXp = 999;
        state.Inventory["carrot"] = 7;
        state.Plots[1].CropId = "pumpkin";
        state.Plots[1].Growth = 12.5;
        state.Settings.StripMode = StripMode.OnHover;
        state.Settings.Size = StripSize.Small;

        saves.Save(state);
        var loaded = saves.LoadOrCreate();

        Assert.Equal(1234, loaded.Coins);
        Assert.Equal(999, loaded.FarmingXp);
        Assert.Equal(7, loaded.Inventory["carrot"]);
        Assert.Equal("pumpkin", loaded.Plots[1].CropId);
        Assert.Equal(12.5, loaded.Plots[1].Growth);
        Assert.Equal(StripMode.OnHover, loaded.Settings.StripMode);
        Assert.Equal(StripSize.Small, loaded.Settings.Size);
        Assert.Contains("\"OnHover\"", File.ReadAllText(SavePath)); // enums are readable in the file
    }

    [Fact]
    public void Broken_save_falls_back_to_the_backup()
    {
        var saves = new SaveManager(SavePath);
        var state = GameState.CreateNew();
        state.Coins = 10;
        saves.Save(state);
        state.Coins = 20;
        saves.Save(state); // the 10-coin save becomes the backup

        File.WriteAllText(SavePath, "{ not json");

        Assert.Equal(10, saves.LoadOrCreate().Coins);
    }

    [Fact]
    public void Normalize_repairs_odd_saves()
    {
        var state = new GameState
        {
            Coins = -5,
            Plots = { new PlotState { CropId = "banana", Growth = 50 }, new PlotState { CropId = "WHEAT", Growth = 9999 } },
            LastPlantedCrop = "banana",
        };
        state.Inventory["banana"] = 3;

        state.Normalize();

        Assert.Equal(0, state.Coins);
        Assert.Equal(GardenProgression.StartingPlots, state.Plots.Count);
        Assert.Null(state.Plots[0].CropId);
        Assert.Equal("wheat", state.Plots[1].CropId);
        Assert.Equal(Crops.Wheat.GrowSeconds, state.Plots[1].Growth);
        Assert.Empty(state.Inventory);
        Assert.Equal("wheat", state.LastPlantedCrop);
    }
}

public class SpriteSheetTests
{
    [Fact]
    public void Built_in_sprites_load_and_include_everything_the_game_draws()
    {
        var sheet = SpriteSheet.LoadDefault();

        string[] required =
        {
            "soil", "soil_locked", "lock", "stage0", "stage1", "stage2",
            "farmer_idle", "farmer_blink", "farmer_walk1", "farmer_walk2", "farmer_harvest",
            "coin", "sparkle",
        };
        foreach (var name in required.Concat(Crops.All.Select(c => c.RipeSprite)))
            Assert.True(sheet.Contains(name), $"sprites.txt is missing [sprite {name}]");

        foreach (var name in required.Where(n => n.StartsWith("farmer") || n.StartsWith("stage") || n.StartsWith("soil"))
                     .Concat(Crops.All.Select(c => c.RipeSprite)))
        {
            Assert.Equal(16, sheet[name].Width);
            Assert.Equal(16, sheet[name].Height);
        }
    }

    [Fact]
    public void Parser_reads_palette_and_pixels()
    {
        var sheet = SpriteSheet.Parse("""
            [palette]
            R = ff0000   red
            h = 80ffffff half-transparent white

            [sprite test]
            R.
            .h
            """);

        var sprite = sheet["test"];
        Assert.Equal(2, sprite.Width);
        Assert.Equal(2, sprite.Height);
        Assert.Equal(new uint[] { 0xFFFF0000, 0, 0, 0x80FFFFFF }, sprite.Pixels);
    }

    [Theory]
    [InlineData("[palette]\nR = ff0000\n[sprite a]\nRR\nR\n", "row 2")]
    [InlineData("[palette]\nR = ff0000\n[sprite a]\nRX\n", "'X'")]
    [InlineData("[palette]\nR = nope\n", "isn't a colour")]
    [InlineData("RR\n", "after a [sprite")]
    public void Parser_explains_mistakes(string text, string expected)
    {
        var error = Assert.Throws<FormatException>(() => SpriteSheet.Parse(text));
        Assert.Contains(expected, error.Message);
    }
}
