using System.Text.Json.Serialization;

namespace TaskBarGame.Core;

/// <summary>
/// Everything that gets written to the save file. Keep this a plain data class:
/// the rules live in <see cref="GameEngine"/>.
/// </summary>
public sealed class GameState
{
    /// <summary>2 = Harvest Edition (one long field row, tools, armour, talismans).</summary>
    public int Version { get; set; } = 2;

    public long Coins { get; set; }
    public double FarmingXp { get; set; }
    public long GardenXp { get; set; }

    /// <summary>Items you are holding (crops, packed crops, dense crops), by item id.</summary>
    public Dictionary<string, long> Inventory { get; set; } = new();

    /// <summary>Total crop items ever harvested, by crop id.</summary>
    public Dictionary<string, long> Collections { get; set; } = new();

    public FieldState Field { get; set; } = new();

    /// <summary>Your hoe for each crop, by crop id.</summary>
    public Dictionary<string, ToolState> Tools { get; set; } = new();

    /// <summary>Armour tier worn in each slot (0 = nothing).</summary>
    public Dictionary<ArmourSlot, int> Armour { get; set; } = new();

    /// <summary>Best tier owned of each talisman (0 = none).</summary>
    public Dictionary<TalismanFamily, int> Talismans { get; set; } = new();

    /// <summary>0 = none, 1 = Compactor, 2 = Dense Compactor.</summary>
    public int Compactor { get; set; }

    public BazaarState Bazaar { get; set; } = new();

    /// <summary>Visitors, the Visitor Shop, the Composter and timed boosts.</summary>
    public GardenState Garden { get; set; } = new();

    /// <summary>Rare finds: how many you've found and are holding, by rare find id.</summary>
    public Dictionary<string, RareFindState> RareFinds { get; set; } = new();

    /// <summary>Where the farmer is, Fishing XP, rods, fishing armour, bait and the catch log.</summary>
    public FishingState Fishing { get; set; } = new();

    /// <summary>Hats and outfits whose unlocking you've been told about.</summary>
    public HashSet<string> UnlockedCosmetics { get; set; } = new();

    public GameSettings Settings { get; set; } = new();
    public GameStats Stats { get; set; } = new();

    public DateTime LastSavedUtc { get; set; } = DateTime.UtcNow;

    public static GameState CreateNew()
    {
        var state = new GameState();
        state.Normalize();
        return state;
    }

    /// <summary>
    /// Fixes anything odd in a loaded save (missing lists, unknown crops, wrong tile count)
    /// so the rest of the game never has to worry about it.
    /// </summary>
    public void Normalize()
    {
        Inventory ??= new();
        Collections ??= new();
        Field ??= new();
        Tools ??= new();
        Armour ??= new();
        Talismans ??= new();
        Bazaar ??= new();
        UnlockedCosmetics ??= new();
        Garden ??= new();
        Fishing ??= new();
        RareFinds ??= new();
        foreach (var id in RareFinds.Keys.ToList())
        {
            var find = RareFinds[id];
            if (Core.RareFinds.Find(id) is null || find is null)
            {
                RareFinds.Remove(id);
                continue;
            }
            find.Found = Math.Max(0, find.Found);
            find.Held = Math.Clamp(find.Held, 0, find.Found);
        }
        Settings ??= new();
        Stats ??= new();

        Coins = Math.Max(0, Coins);
        FarmingXp = double.IsFinite(FarmingXp) ? Math.Max(0, FarmingXp) : 0;
        GardenXp = Math.Max(0, GardenXp);

        foreach (var key in Inventory.Keys.ToList())
            if (!Items.TryGet(key, out _) || Inventory[key] <= 0) Inventory.Remove(key);
        foreach (var key in Collections.Keys.ToList())
            if (!Crops.TryGet(key, out _) || Collections[key] < 0) Collections.Remove(key);
        foreach (var key in Tools.Keys.ToList())
        {
            var tool = Tools[key];
            if (!Crops.TryGet(key, out _) || tool is null)
            {
                Tools.Remove(key);
                continue;
            }
            tool.Tier = Math.Clamp(tool.Tier, 0, Core.Tools.MaxTier);
            tool.Breaks = Math.Max(0, tool.Breaks);
        }
        foreach (var slot in Armour.Keys.ToList())
            Armour[slot] = Math.Clamp(Armour[slot], 0, Core.Armour.MaxTier);
        foreach (var family in Talismans.Keys.ToList())
            Talismans[family] = Math.Clamp(Talismans[family], 0, Core.Talismans.MaxTier);
        Compactor = Math.Clamp(Compactor, 0, Compactors.MaxTier);

        Field.Normalize();
        Bazaar.Normalize();
        Garden.Normalize();
        Fishing.Normalize();
        Settings.Normalize();

        if (LastSavedUtc > DateTime.UtcNow || LastSavedUtc == default)
            LastSavedUtc = DateTime.UtcNow;
    }
}

/// <summary>The field: one long row of a single crop.</summary>
public sealed class FieldState
{
    public string CropId { get; set; } = Crops.Wheat.Id;

    public int Length { get; set; } = FieldProgression.StartLength;

    /// <summary>Seconds of regrowth for each crop in the row. Grown at the crop's RegrowSeconds.</summary>
    public List<double> Tiles { get; set; } = new();

    public void Normalize()
    {
        if (!Crops.TryGet(CropId, out var crop))
        {
            crop = Crops.Wheat;
            Tiles?.Clear();
        }
        CropId = crop.Id;

        int[] lengths = FieldProgression.Expansions.Select(e => e.Length).Prepend(FieldProgression.StartLength).ToArray();
        Length = lengths.Contains(Length) ? Length : lengths.Where(l => l <= Length).DefaultIfEmpty(FieldProgression.StartLength).Max();

        Tiles ??= new();
        if (Tiles.Count > Length) Tiles.RemoveRange(Length, Tiles.Count - Length);
        while (Tiles.Count < Length) Tiles.Add(0);
        for (int i = 0; i < Tiles.Count; i++)
            Tiles[i] = double.IsFinite(Tiles[i]) ? Math.Clamp(Tiles[i], 0, crop.RegrowSeconds) : 0;
    }
}

public sealed class ToolState
{
    /// <summary>0 = no hoe yet, 1-3 = Basic, Reinforced, Masterwork.</summary>
    public int Tier { get; set; }

    /// <summary>Crops broken with this hoe (any tier). Adds Harvest Fortune over time.</summary>
    public long Breaks { get; set; }
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

    /// <summary>The farmer walks the row and breaks grown crops by themselves.</summary>
    public bool FarmerAutoHarvest { get; set; } = true;

    /// <summary>Crops (by crop id) that are sold straight away when they're harvested.</summary>
    public HashSet<string> AutoSellCrops { get; set; } = new();

    /// <summary>Fishing items (by item id) that are sold straight away when they're caught.</summary>
    public HashSet<string> AutoSellItems { get; set; } = new();

    /// <summary>Items (by item id) that are never sold, by any button.</summary>
    public HashSet<string> LockedItems { get; set; } = new();

    /// <summary>
    /// Selling to the Market (Sell, Sell all crops and auto-sell) keeps back what your next upgrades
    /// need, and the Compactor leaves those crops unpacked.
    /// </summary>
    public bool KeepForUpgrades { get; set; } = true;

    /// <summary>Crops (by crop id) the Compactor leaves alone.</summary>
    public HashSet<string> CompactorSkips { get; set; } = new();

    public bool ShowFloatingText { get; set; } = true;

    /// <summary>Hide the strip while a fullscreen app (game, video) is in front.</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Pop-ups that are turned off.</summary>
    public HashSet<PopupKind> HiddenPopups { get; set; } = new();

    /// <summary>No pop-ups at all for now (except ones for buttons you press).</summary>
    public bool PausePopups { get; set; }

    /// <summary>Look for a new version on GitHub now and then.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Install new versions without asking first (otherwise a pop-up asks).</summary>
    public bool InstallUpdatesAutomatically { get; set; }

    /// <summary>Show big numbers as "1.2M" instead of "1,234,567".</summary>
    public bool ShortNumbers { get; set; }

    /// <summary>Update the strip less often while the laptop is on battery.</summary>
    public bool BatterySaver { get; set; } = true;

    /// <summary>The monitor the strip sits on (Windows' name for it, like \\.\DISPLAY2); null = the main one.</summary>
    public string? Monitor { get; set; }

    /// <summary>Keyboard shortcut that opens or closes the panel from any app ("" = none).</summary>
    public string PanelHotkey { get; set; } = "Ctrl+Alt+F";

    /// <summary>Keyboard shortcut that hides or shows the strip from any app ("" = none).</summary>
    public string StripHotkey { get; set; } = "Ctrl+Alt+H";

    /// <summary>The farmer's hat and outfit (see <see cref="Cosmetics"/>).</summary>
    public string Hat { get; set; } = Cosmetics.DefaultHat;
    public string Outfit { get; set; } = Cosmetics.DefaultOutfit;

    /// <summary>Where the panel was last moved to (null = above the strip).</summary>
    public double? PanelLeft { get; set; }
    public double? PanelTop { get; set; }

    /// <summary>The panel tab that was open last, by name ("FishingTab").</summary>
    public string? PanelTabName { get; set; }

    /// <summary>
    /// Old saves remembered the tab by its number (before the Fishing tab moved them along). Only read
    /// when <see cref="PanelTabName"/> isn't set.
    /// </summary>
    public int PanelTab { get; set; }

    /// <summary>
    /// Old saves had one "sell everything automatically" switch. Only read when loading:
    /// it's turned into <see cref="AutoSellCrops"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoSell { get; set; }

    public bool IsPopupShown(PopupKind kind) => !PausePopups && !HiddenPopups.Contains(kind);

    public void Normalize()
    {
        AutoSellCrops ??= new();
        AutoSellItems ??= new();
        LockedItems ??= new();
        CompactorSkips ??= new();
        HiddenPopups ??= new();
        PanelHotkey ??= "";
        StripHotkey ??= "";
        if (Cosmetics.Hats.All(c => c.Id != Hat)) Hat = Cosmetics.DefaultHat;
        if (Cosmetics.Outfits.All(c => c.Id != Outfit)) Outfit = Cosmetics.DefaultOutfit;

        if (AutoSell == true)
            foreach (var crop in Crops.All) AutoSellCrops.Add(crop.Id);
        AutoSell = null;

        AutoSellCrops.RemoveWhere(id => !Crops.TryGet(id, out _));
        CompactorSkips.RemoveWhere(id => !Crops.TryGet(id, out _));
        AutoSellItems.RemoveWhere(id => !Items.TryGet(id, out var item) || item.IsCrop || item.Kind == ItemKind.Bait);
        LockedItems.RemoveWhere(id => !Items.TryGet(id, out _));
        if (PanelLeft is { } left && !double.IsFinite(left)) PanelLeft = null;
        if (PanelTop is { } top && !double.IsFinite(top)) PanelTop = null;
        PanelTab = Math.Max(0, PanelTab);
    }
}

/// <summary>The kinds of pop-up the strip shows, each of which can be turned off.</summary>
public enum PopupKind
{
    LevelUp,
    Collection,
    Crafted,
    BazaarOrder,
    UpgradeReady,
    WelcomeBack,
    Update,
    Cosmetic,
    Visitor,
    RareDrop,
    Treasure,
    BaitRanOut,
}

/// <summary>Everything about fishing that's saved.</summary>
public sealed class FishingState
{
    /// <summary>Where the farmer is: "field", or a lake id (see <see cref="Places"/>).</summary>
    public string Location { get; set; } = Places.Field;

    /// <summary>The lake the farmer fished on last (where "Go fishing" goes).</summary>
    public string LastLake { get; set; } = Places.WillowLake.Id;

    public double Xp { get; set; }

    /// <summary>1-5: Fishing Rod, Angler's, Lakewarden, Stormcatcher, Tidecaller.</summary>
    public int Rod { get; set; } = 1;

    /// <summary>Fishing armour tier worn in each slot (0 = nothing).</summary>
    public Dictionary<ArmourSlot, int> Armour { get; set; } = new();

    /// <summary>The chosen bait's item id (null = no bait). One is used up on every catch.</summary>
    public string? Bait { get; set; }

    /// <summary>Lakes on other islands you've bought the boat trip to.</summary>
    public HashSet<string> BoatTrips { get; set; } = new();

    /// <summary>Everything ever caught, by item id: the catch log.</summary>
    public Dictionary<string, long> Caught { get; set; } = new();

    /// <summary>Cosmetics bought from Rusty Rowan.</summary>
    public HashSet<string> OwnedCosmetics { get; set; } = new();

    public long Catches { get; set; }
    public long Treasures { get; set; }

    public void Normalize()
    {
        Armour ??= new();
        BoatTrips ??= new();
        Caught ??= new();
        OwnedCosmetics ??= new();
        if (!Places.IsKnown(Location)) Location = Places.Field;
        if (Places.FindLake(LastLake) is not { } last || (last.IsOnAnotherIsland && !BoatTrips.Contains(last.Id)))
            LastLake = Places.WillowLake.Id;
        if (Places.FindLake(Location) is { IsOnAnotherIsland: true } && !BoatTrips.Contains(Location)) Location = Places.Field;
        Xp = double.IsFinite(Xp) ? Math.Max(0, Xp) : 0;
        Rod = Math.Clamp(Rod, 1, Rods.MaxTier);
        foreach (var slot in Armour.Keys.ToList())
            Armour[slot] = Math.Clamp(Armour[slot], 0, FishingArmour.MaxTier);
        if (!Baits.IsBait(Bait)) Bait = null;
        BoatTrips.RemoveWhere(id => Places.FindLake(id) is not { IsOnAnotherIsland: true });
        foreach (var key in Caught.Keys.ToList())
            if (!Items.TryGet(key, out var item) || item.IsCrop || Caught[key] <= 0) Caught.Remove(key);
        Catches = Math.Max(0, Catches);
        Treasures = Math.Max(0, Treasures);
    }
}

public sealed class GameStats
{
    public long TotalBreaks { get; set; }
    public long HandBreaks { get; set; }
    public long CoinsEarned { get; set; }
    public long ItemsCrafted { get; set; }
}
