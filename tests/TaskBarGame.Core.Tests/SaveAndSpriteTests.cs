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
        Assert.Equal(FieldProgression.StartLength, state.Field.Tiles.Count);
        Assert.Equal(2, state.Version);
    }

    [Fact]
    public void Default_save_is_separate_from_the_original_game()
    {
        Assert.Contains("TaskBarGame Harvest Edition", new SaveManager().SavePath);
    }

    [Fact]
    public void Save_and_load_round_trips()
    {
        var saves = new SaveManager(SavePath);
        var state = GameState.CreateNew();
        state.Coins = 1234;
        state.FarmingXp = 999.5;
        state.Inventory["packed_carrot"] = 7;
        state.Field.CropId = "pumpkin";
        state.Field.Tiles[2] = 2.5;
        state.Tools["wheat"] = new ToolState { Tier = 2, Breaks = 4321 };
        state.Armour[ArmourSlot.Chestplate] = 3;
        state.Talismans[TalismanFamily.Swiftstep] = 1;
        state.Settings.StripMode = StripMode.OnHover;
        state.Compactor = 2;
        state.Settings.AutoSellCrops.Add("melon");
        state.Settings.LockedItems.Add("packed_carrot");
        state.Settings.CompactorSkips.Add("wheat");
        state.Settings.HiddenPopups.Add(PopupKind.Collection);
        state.Settings.PanelLeft = 120.5;
        state.Settings.PanelTab = 4;

        saves.Save(state);
        var loaded = saves.LoadOrCreate();

        Assert.Equal(1234, loaded.Coins);
        Assert.Equal(999.5, loaded.FarmingXp);
        Assert.Equal(7, loaded.Inventory["packed_carrot"]);
        Assert.Equal("pumpkin", loaded.Field.CropId);
        Assert.Equal(2.5, loaded.Field.Tiles[2]);
        Assert.Equal(2, loaded.Tools["wheat"].Tier);
        Assert.Equal(4321, loaded.Tools["wheat"].Breaks);
        Assert.Equal(3, loaded.Armour[ArmourSlot.Chestplate]);
        Assert.Equal(1, loaded.Talismans[TalismanFamily.Swiftstep]);
        Assert.Equal(StripMode.OnHover, loaded.Settings.StripMode);
        Assert.Equal(2, loaded.Compactor);
        Assert.Equal(new[] { "melon" }, loaded.Settings.AutoSellCrops);
        Assert.Equal(new[] { "packed_carrot" }, loaded.Settings.LockedItems);
        Assert.Equal(new[] { "wheat" }, loaded.Settings.CompactorSkips);
        Assert.Equal(new[] { PopupKind.Collection }, loaded.Settings.HiddenPopups);
        Assert.Equal(120.5, loaded.Settings.PanelLeft);
        Assert.Null(loaded.Settings.PanelTop);
        Assert.Equal(4, loaded.Settings.PanelTab);
        string json = File.ReadAllText(SavePath);
        Assert.Contains("\"Chestplate\"", json); // readable names in the file
        Assert.Contains("\"Swiftstep\"", json);
        Assert.DoesNotContain("\"AutoSell\"", json); // the old setting is only read, never written
    }

    [Fact]
    public void Old_auto_sell_setting_turns_into_auto_sell_for_every_crop()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SavePath, """{ "Version": 2, "Settings": { "AutoSell": true } }""");
        var loaded = new SaveManager(SavePath).LoadOrCreate();

        Assert.Equal(Crops.All.Count, loaded.Settings.AutoSellCrops.Count);
        Assert.Null(loaded.Settings.AutoSell);
        Assert.True(loaded.Settings.KeepForUpgrades);
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
            Field = new FieldState { CropId = "banana", Length = 13, Tiles = { 99, double.NaN } },
            Tools = { ["banana"] = new ToolState { Tier = 2 }, ["wheat"] = new ToolState { Tier = 9, Breaks = -3 } },
            Armour = { [ArmourSlot.Boots] = 7 },
            Talismans = { [TalismanFamily.Clover] = -1 },
        };
        state.Inventory["banana"] = 3;
        state.Inventory["packed_wheat"] = 0;

        state.Normalize();

        Assert.Equal(0, state.Coins);
        Assert.Equal("wheat", state.Field.CropId);
        Assert.Equal(12, state.Field.Length); // not a real length: back to the expansion below it
        Assert.Equal(12, state.Field.Tiles.Count);
        Assert.All(state.Field.Tiles, t => Assert.InRange(t, 0, Crops.Wheat.RegrowSeconds));
        Assert.False(state.Tools.ContainsKey("banana"));
        Assert.Equal(Tools.MaxTier, state.Tools["wheat"].Tier);
        Assert.Equal(0, state.Tools["wheat"].Breaks);
        Assert.Equal(Armour.MaxTier, state.Armour[ArmourSlot.Boots]);
        Assert.Equal(0, state.Talismans[TalismanFamily.Clover]);
        Assert.Empty(state.Inventory);
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
            "farmer_idle", "farmer_blink", "farmer_walk1", "farmer_walk2",
            "farmer_farm1", "farmer_farm2", "farmer_swing",
            "coin", "sparkle", "sack", "crate",
            "icon_helmet", "icon_chestplate", "icon_leggings", "icon_boots",
            "icon_charm", "icon_ring", "icon_relic", "icon_compactor",
            "hat_cap", "hat_bandana", "hat_tophat", "hat_flowers", "hat_crown",
        };
        foreach (var name in required.Concat(Crops.All.Select(c => c.RipeSprite)))
            Assert.True(sheet.Contains(name), $"sprites.txt is missing [sprite {name}]");

        foreach (var name in required.Where(n => n.StartsWith("farmer") || n.StartsWith("stage") || n.StartsWith("soil") || n.StartsWith("icon") || n.StartsWith("hat"))
                     .Concat(Crops.All.Select(c => c.RipeSprite)))
        {
            Assert.Equal(16, sheet[name].Width);
            Assert.Equal(16, sheet[name].Height);
        }
    }

    [Fact]
    public void Every_crop_has_a_tool_with_a_picture()
    {
        // The pictures live in the app (src/TaskBarGame.App/Assets/Tools); find them from the test's folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskBarGame.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string tools = Path.Combine(dir!.FullName, "src", "TaskBarGame.App", "Assets", "Tools");

        foreach (var crop in Crops.All)
        {
            Assert.True(File.Exists(Path.Combine(tools, Tools.Texture(crop) + ".png")), $"{crop.Name}: missing {Tools.Texture(crop)}.png");
            Assert.DoesNotContain("Hoe", Tools.Name(crop, 1).Replace("Carrot Hoe", ""));
        }
        Assert.Equal("Reinforced Pumpkin Dicer", Tools.Name(Crops.Pumpkin, 2));
        Assert.Equal(Crops.All.Count, Crops.All.Select(Tools.Texture).Distinct().Count());
    }

    [Fact]
    public void Recolourable_palette_colours_are_defined()
    {
        var palette = SpriteSheet.LoadDefault().Palette;
        foreach (char c in "AaIiEZNnb")
            Assert.True(palette.ContainsKey(c), $"palette is missing '{c}'");
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
