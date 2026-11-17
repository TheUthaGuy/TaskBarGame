namespace TaskBarGame.Core;

/// <summary>
/// Everything the game needs to know about one kind of crop.
/// </summary>
/// <param name="Id">Save-file id. Never rename an id once players have saves.</param>
/// <param name="Name">Name shown to the player.</param>
/// <param name="RegrowSeconds">Seconds for a broken crop to grow back.</param>
/// <param name="DropsPerBreak">Items from breaking one crop, before Harvest Fortune.</param>
/// <param name="SellPrice">Coins the Market pays for one item.</param>
/// <param name="FarmingXp">Farming skill XP for breaking one crop.</param>
/// <param name="UnlockGardenLevel">Garden level needed before you can plant it.</param>
/// <param name="ColorHex">Accent colour used in the UI (progress bars, text).</param>
public sealed record CropDef(
    string Id,
    string Name,
    double RegrowSeconds,
    int DropsPerBreak,
    int SellPrice,
    int FarmingXp,
    int UnlockGardenLevel,
    string ColorHex)
{
    /// <summary>Sprite name of the fully grown crop in sprites.txt.</summary>
    public string RipeSprite => Id + "_ripe";

    /// <summary>Coins one break is worth before fortune.</summary>
    public int CoinsPerBreak => DropsPerBreak * SellPrice;
}

/// <summary>
/// The list of all crops. Each new crop is worth more per break and more per second of
/// regrowing, so unlocking one is always an upgrade.
/// </summary>
public static class Crops
{
    //                         id            name          regrow drops sell  XP  lvl  colour
    public static readonly CropDef Wheat     = new("wheat",     "Wheat",          5,  1,   2,  1,  1, "#E8C34A");
    public static readonly CropDef Carrot    = new("carrot",    "Carrot",         6,  3,   1,  1,  2, "#F39A3C");
    public static readonly CropDef Potato    = new("potato",    "Potato",         6,  4,   1,  1,  3, "#C9A26B");
    public static readonly CropDef SugarCane = new("sugarcane", "Sugar Cane",     7,  2,   3,  2,  4, "#8BD45A");
    public static readonly CropDef Pumpkin   = new("pumpkin",   "Pumpkin",        9,  1,   9,  3,  5, "#F07F1E");
    public static readonly CropDef Melon     = new("melon",     "Melon",          9,  5,   2,  3,  6, "#6CC24A");
    public static readonly CropDef Cocoa     = new("cocoa",     "Cocoa Beans",   11,  3,   5,  4,  8, "#A0683C");
    public static readonly CropDef Cactus    = new("cactus",    "Cactus",        12,  2,   9,  4, 10, "#49B07C");
    public static readonly CropDef Mushroom  = new("mushroom",  "Mushroom",      14,  1,  24,  5, 12, "#E04A3C");
    public static readonly CropDef Moonbloom = new("moonbloom", "Moonbloom",     20,  1,  40,  8, 15, "#A97FDC");

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
