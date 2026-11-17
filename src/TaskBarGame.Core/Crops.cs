namespace TaskBarGame.Core;

/// <summary>
/// Everything the game needs to know about one kind of crop.
/// </summary>
/// <param name="Id">Save-file id. Never rename an id once players have saves.</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="GrowSeconds">Seconds from planting until the crop is ripe.</param>
/// <param name="BaseDrops">Items you get from one harvest before Harvest Fortune.</param>
/// <param name="SellPrice">Coins the Market pays for one item.</param>
/// <param name="FarmingXp">Farming skill XP for one harvest.</param>
/// <param name="GardenXp">Garden XP for one harvest.</param>
/// <param name="UnlockGardenLevel">Garden level needed before you can plant it.</param>
/// <param name="ColorHex">Accent colour used in the UI (progress bars, text).</param>
public sealed record CropDef(
    string Id,
    string Name,
    double GrowSeconds,
    int BaseDrops,
    int SellPrice,
    int FarmingXp,
    int GardenXp,
    int UnlockGardenLevel,
    string ColorHex)
{
    /// <summary>Sprite name of the fully grown crop in sprites.txt.</summary>
    public string RipeSprite => Id + "_ripe";
}

/// <summary>
/// The list of all crops. Each new crop earns a little more per minute than the last,
/// so unlocking one always feels like an upgrade.
/// </summary>
public static class Crops
{
    //                         id            name          grow  drops sell  fXP gXP lvl  colour
    public static readonly CropDef Wheat     = new("wheat",     "Wheat",       30, 3,   2,   3,  1,  1, "#E8C34A");
    public static readonly CropDef Carrot    = new("carrot",    "Carrot",      40, 5,   2,   5,  1,  2, "#F39A3C");
    public static readonly CropDef Potato    = new("potato",    "Potato",      50, 5,   3,   7,  1,  3, "#C9A26B");
    public static readonly CropDef SugarCane = new("sugarcane", "Sugar Cane",  60, 4,   5,   9,  2,  4, "#8BD45A");
    public static readonly CropDef Pumpkin   = new("pumpkin",   "Pumpkin",     90, 2,  18,  16,  2,  5, "#F07F1E");
    public static readonly CropDef Melon     = new("melon",     "Melon",      100, 9,   5,  20,  3,  6, "#6CC24A");
    public static readonly CropDef Cocoa     = new("cocoa",     "Cocoa Beans",120, 4,  15,  26,  3,  8, "#A0683C");
    public static readonly CropDef Cactus    = new("cactus",    "Cactus",     150, 5,  16,  35,  4, 10, "#49B07C");
    public static readonly CropDef Mushroom  = new("mushroom",  "Mushroom",   180, 3,  35,  45,  4, 12, "#E04A3C");
    public static readonly CropDef Moonbloom = new("moonbloom", "Moonbloom",  480, 2, 160, 140, 10, 15, "#A97FDC");

    public static readonly IReadOnlyList<CropDef> All = new[]
    {
        Wheat, Carrot, Potato, SugarCane, Pumpkin, Melon, Cocoa, Cactus, Mushroom, Moonbloom,
    };

    private static readonly Dictionary<string, CropDef> ById =
        All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? id, out CropDef crop)
    {
        if (id is not null && ById.TryGetValue(id, out var found))
        {
            crop = found;
            return true;
        }
        crop = null!;
        return false;
    }

    public static CropDef Get(string id) =>
        TryGet(id, out var crop) ? crop : throw new KeyNotFoundException($"Unknown crop '{id}'.");
}
