namespace TaskBarGame.Core;

public enum ItemKind
{
    /// <summary>A crop straight from the field.</summary>
    Crop,

    /// <summary>160 crops packed together.</summary>
    Packed,

    /// <summary>160 packed crops pressed together (25,600 crops).</summary>
    Dense,

    /// <summary>A fish caught on a lake.</summary>
    Fish,

    /// <summary>Something else a lake gives you: lily pads, clay, ink sacs...</summary>
    LakeDrop,

    /// <summary>A treasure catch: a crafting material for rods, fishing armour and more.</summary>
    Treasure,

    /// <summary>Rubbish from Driftwood Cove, which Rusty Rowan takes in trade.</summary>
    Junk,

    /// <summary>Bait: one is used up on every catch while it's chosen.</summary>
    Bait,
}

/// <summary>
/// Anything to do with fishing that you can hold: fish, lake drops, treasure, junk and bait.
/// </summary>
/// <param name="Id">Save-file id. Never rename an id once players have saves.</param>
/// <param name="SellPrice">Coins the Market pays for one.</param>
/// <param name="Sprite">Its picture in sprites.txt.</param>
/// <param name="ColorHex">Accent colour for its name in pop-ups and floating text.</param>
public sealed record FishingItemDef(
    string Id,
    string Name,
    ItemKind Kind,
    long SellPrice,
    string Sprite,
    string ColorHex,
    string Description = "");

/// <summary>
/// Something you can hold in your inventory: a crop (plain, packed or dense) or a fishing item.
/// Exactly one of <see cref="Crop"/> and <see cref="Fishing"/> is set.
/// </summary>
public sealed record ItemDef
{
    /// <summary>How many of the tier below make one of this.</summary>
    public const int PackSize = 160;

    /// <summary>How many crops one fishing item counts as when it moves a Bazaar price.</summary>
    public const int FishingItemBazaarWeight = 40;

    private ItemDef(string id, ItemKind kind, CropDef? crop, FishingItemDef? fishing)
    {
        Id = id;
        Kind = kind;
        Crop = crop;
        Fishing = fishing;
    }

    public static ItemDef ForCrop(CropDef crop, ItemKind kind)
    {
        string id = kind switch
        {
            ItemKind.Packed => "packed_" + crop.Id,
            ItemKind.Dense => "dense_" + crop.Id,
            ItemKind.Crop => crop.Id,
            _ => throw new ArgumentException("Crops are plain, packed or dense.", nameof(kind)),
        };
        return new ItemDef(id, kind, crop, null);
    }

    public static ItemDef ForFishing(FishingItemDef fishing) => new(fishing.Id, fishing.Kind, null, fishing);

    public string Id { get; }
    public ItemKind Kind { get; }

    /// <summary>The crop this is (plain, packed or dense), or null for fishing items.</summary>
    public CropDef? Crop { get; }

    /// <summary>The fishing item this is, or null for crops.</summary>
    public FishingItemDef? Fishing { get; }

    public bool IsCrop => Crop is not null;

    public string Name => Kind switch
    {
        ItemKind.Packed => "Packed " + Crop!.Name,
        ItemKind.Dense => "Dense " + Crop!.Name,
        ItemKind.Crop => Crop!.Name,
        _ => Fishing!.Name,
    };

    public string ColorHex => Crop?.ColorHex ?? Fishing!.ColorHex;

    /// <summary>How many plain crops this is made of (1 for fishing items).</summary>
    public long CropsInside => Kind switch
    {
        ItemKind.Packed => PackSize,
        ItemKind.Dense => (long)PackSize * PackSize,
        _ => 1,
    };

    /// <summary>Coins the Market pays for one. Packing never loses value: a packed crop sells for 160 crops.</summary>
    public long SellPrice => Crop is { } crop ? crop.SellPrice * CropsInside : Fishing!.SellPrice;

    /// <summary>How many crops' worth this counts as when trading moves a Bazaar price.</summary>
    public long BazaarWeight => IsCrop ? CropsInside : FishingItemBazaarWeight;

    /// <summary>What this packs into, or null (dense crops and fishing items don't pack).</summary>
    public ItemDef? PacksInto => Kind switch
    {
        ItemKind.Crop => Items.Packed(Crop!),
        ItemKind.Packed => Items.Dense(Crop!),
        _ => null,
    };
}

public static class Items
{
    private static readonly Dictionary<string, ItemDef> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ItemDef> InOrder = new();

    static Items()
    {
        foreach (var crop in Crops.All)
        {
            Add(ItemDef.ForCrop(crop, ItemKind.Crop));
            Add(ItemDef.ForCrop(crop, ItemKind.Packed));
            Add(ItemDef.ForCrop(crop, ItemKind.Dense));
        }
        foreach (var fishing in FishingItems.All)
            Add(ItemDef.ForFishing(fishing));
    }

    private static void Add(ItemDef item)
    {
        ById.Add(item.Id, item);
        InOrder.Add(item);
    }

    /// <summary>
    /// Every item: crop by crop (Wheat, Packed Wheat, Dense Wheat, Carrot, ...), then the fishing
    /// items (fish, lake drops, treasure, junk, bait).
    /// </summary>
    public static IReadOnlyList<ItemDef> All => InOrder;

    /// <summary>Just the crop items (plain, packed and dense).</summary>
    public static IEnumerable<ItemDef> CropItems => InOrder.Where(i => i.IsCrop);

    /// <summary>Just the fishing items.</summary>
    public static IEnumerable<ItemDef> FishingItemDefs => InOrder.Where(i => !i.IsCrop);

    public static ItemDef Raw(CropDef crop) => ById[crop.Id];
    public static ItemDef Packed(CropDef crop) => ById["packed_" + crop.Id];
    public static ItemDef Dense(CropDef crop) => ById["dense_" + crop.Id];

    public static ItemDef Of(FishingItemDef fishing) => ById[fishing.Id];

    public static bool TryGet(string? id, out ItemDef item)
    {
        if (id is not null && ById.TryGetValue(id, out var found))
        {
            item = found;
            return true;
        }
        item = null!;
        return false;
    }

    public static ItemDef Get(string id) =>
        TryGet(id, out var item) ? item : throw new KeyNotFoundException($"Unknown item '{id}'.");
}
