namespace TaskBarGame.Core;

/// <summary>
/// Everything that gets written to the save file. Keep this a plain data class:
/// the rules live in <see cref="GameEngine"/>.
/// </summary>
public sealed class GameState
{
    public int Version { get; set; } = 1;

    public long Coins { get; set; }
    public long FarmingXp { get; set; }
    public long GardenXp { get; set; }

    /// <summary>Crop items you are holding, by crop id. Sell them at the Market.</summary>
    public Dictionary<string, long> Inventory { get; set; } = new();

    /// <summary>Total crop items ever harvested, by crop id.</summary>
    public Dictionary<string, long> Collections { get; set; } = new();

    public List<PlotState> Plots { get; set; } = new();

    /// <summary>The crop new plots get planted with.</summary>
    public string LastPlantedCrop { get; set; } = Crops.Wheat.Id;

    public GameSettings Settings { get; set; } = new();
    public GameStats Stats { get; set; } = new();

    public DateTime LastSavedUtc { get; set; } = DateTime.UtcNow;

    public static GameState CreateNew()
    {
        var state = new GameState();
        for (int i = 0; i < GardenProgression.StartingPlots; i++)
            state.Plots.Add(new PlotState { CropId = Crops.Wheat.Id });
        return state;
    }

    /// <summary>
    /// Fixes anything odd in a loaded save (missing lists, unknown crops, too many plots)
    /// so the rest of the game never has to worry about it.
    /// </summary>
    public void Normalize()
    {
        Inventory ??= new();
        Collections ??= new();
        Plots ??= new();
        Settings ??= new();
        Stats ??= new();

        Coins = Math.Max(0, Coins);
        FarmingXp = Math.Max(0, FarmingXp);
        GardenXp = Math.Max(0, GardenXp);

        foreach (var key in Inventory.Keys.ToList())
            if (!Crops.TryGet(key, out _) || Inventory[key] <= 0) Inventory.Remove(key);
        foreach (var key in Collections.Keys.ToList())
            if (!Crops.TryGet(key, out _) || Collections[key] < 0) Collections.Remove(key);

        Plots.RemoveAll(p => p is null);
        while (Plots.Count < GardenProgression.StartingPlots)
            Plots.Add(new PlotState { CropId = Crops.Wheat.Id });
        if (Plots.Count > GardenProgression.MaxPlots)
            Plots.RemoveRange(GardenProgression.MaxPlots, Plots.Count - GardenProgression.MaxPlots);

        foreach (var plot in Plots)
        {
            if (!Crops.TryGet(plot.CropId, out var crop))
            {
                plot.CropId = null;
                plot.Growth = 0;
                continue;
            }
            plot.CropId = crop.Id;
            plot.Growth = double.IsFinite(plot.Growth) ? Math.Clamp(plot.Growth, 0, crop.GrowSeconds) : 0;
        }

        if (!Crops.TryGet(LastPlantedCrop, out _))
            LastPlantedCrop = Crops.Wheat.Id;

        if (LastSavedUtc > DateTime.UtcNow || LastSavedUtc == default)
            LastSavedUtc = DateTime.UtcNow;
    }
}

public sealed class PlotState
{
    /// <summary>Crop growing here, or null for an empty plot.</summary>
    public string? CropId { get; set; }

    /// <summary>Seconds of growth so far. Ripe when it reaches the crop's GrowSeconds.</summary>
    public double Growth { get; set; }
}

public enum StripMode
{
    /// <summary>The garden strip is always visible above the taskbar.</summary>
    Always,

    /// <summary>Only the farmer is visible; hover over them to show the strip.</summary>
    OnHover,
}

public enum StripSize { Small, Normal, Large }

public enum StripAnchor { Left, Center, Right }

public sealed class GameSettings
{
    public StripMode StripMode { get; set; } = StripMode.Always;
    public StripSize Size { get; set; } = StripSize.Normal;
    public StripAnchor Anchor { get; set; } = StripAnchor.Left;

    /// <summary>The farmer walks around and harvests ripe crops by themselves.</summary>
    public bool FarmerAutoHarvest { get; set; } = true;

    /// <summary>Harvested crops are sold straight away instead of going to your inventory.</summary>
    public bool AutoSell { get; set; }

    public bool ShowFloatingText { get; set; } = true;

    /// <summary>Hide the strip while a fullscreen app (game, video) is in front.</summary>
    public bool HideInFullscreen { get; set; } = true;
}

public sealed class GameStats
{
    public long TotalHarvests { get; set; }
    public long HandHarvests { get; set; }
    public long CoinsEarned { get; set; }
}
