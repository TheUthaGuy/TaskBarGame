namespace TaskBarGame.Core;

public enum ItemKind
{
    /// <summary>A crop straight from the field.</summary>
    Crop,

    /// <summary>160 crops packed together.</summary>
    Packed,

    /// <summary>160 packed crops pressed together (25,600 crops).</summary>
    Dense,
}

/// <summary>Something you can hold in your inventory: a crop, a packed crop or a dense crop.</summary>
public sealed record ItemDef(string Id, CropDef Crop, ItemKind Kind)
{
    /// <summary>How many of the tier below make one of this.</summary>
    public const int PackSize = 160;

    public string Name => Kind switch
    {
        ItemKind.Packed => "Packed " + Crop.Name,
        ItemKind.Dense => "Dense " + Crop.Name,
        _ => Crop.Name,
    };

    /// <summary>How many plain crops this is made of.</summary>
    public long CropsInside => Kind switch
    {
        ItemKind.Packed => PackSize,
        ItemKind.Dense => (long)PackSize * PackSize,
        _ => 1,
    };

    /// <summary>Packing never loses value: a packed crop sells for 160 crops.</summary>
    public long SellPrice => Crop.SellPrice * CropsInside;

    /// <summary>What this packs into, or null for dense items.</summary>
    public ItemDef? PacksInto => Kind switch
    {
        ItemKind.Crop => Items.Packed(Crop),
        ItemKind.Packed => Items.Dense(Crop),
        _ => null,
    };
}

public static class Items
{
    private static readonly Dictionary<string, ItemDef> ById = new(StringComparer.OrdinalIgnoreCase);

    static Items()
    {
        foreach (var crop in Crops.All)
        {
            Add(new ItemDef(crop.Id, crop, ItemKind.Crop));
            Add(new ItemDef("packed_" + crop.Id, crop, ItemKind.Packed));
            Add(new ItemDef("dense_" + crop.Id, crop, ItemKind.Dense));
        }
    }

    private static void Add(ItemDef item) => ById.Add(item.Id, item);

    /// <summary>Every item, crop by crop: Wheat, Packed Wheat, Dense Wheat, Carrot, ...</summary>
    public static IEnumerable<ItemDef> All => Crops.All.SelectMany(c => new[] { Raw(c), Packed(c), Dense(c) });

    public static ItemDef Raw(CropDef crop) => ById[crop.Id];
    public static ItemDef Packed(CropDef crop) => ById["packed_" + crop.Id];
    public static ItemDef Dense(CropDef crop) => ById["dense_" + crop.Id];

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
