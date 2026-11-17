namespace TaskBarGame.Core;

/// <summary>How much of one crop your next upgrades need, at each packing level.</summary>
/// <param name="Raw">Plain crops: needed as they are, plus enough to pack into the Packed crops you're short of.</param>
/// <param name="Packed">Packed crops: needed as they are, plus enough to press into the Dense crops you're short of.</param>
/// <param name="Dense">Dense crops needed.</param>
public readonly record struct CropNeeds(long Raw, long Packed, long Dense);

/// <summary>
/// Helpers for planning: what your next upgrades need, keeping it safe from selling, the Compactor,
/// buying missing ingredients, and how many coins each crop makes.
/// </summary>
public sealed partial class GameEngine
{
    // ================================================================= what's next

    /// <summary>Every hoe, armour slot, talisman and the Compactor: everything that can be upgraded.</summary>
    public static IEnumerable<(UpgradeKind Kind, string Target)> UpgradeTargets =>
        Crops.All.Select(c => (UpgradeKind.Tool, c.Id))
            .Concat(Armour.Slots.Select(s => (UpgradeKind.Armour, s.ToString())))
            .Concat(Talismans.Families.Select(f => (UpgradeKind.Talisman, f.ToString())))
            .Append((UpgradeKind.Compactor, Compactors.Target));

    /// <summary>
    /// The next upgrade of everything you could craft right now if you had the ingredients and coins
    /// (your Garden level is high enough and you own the tier below).
    /// </summary>
    public IReadOnlyList<Recipe> AvailableNextRecipes()
    {
        // This is asked on every crop broken (for auto-sell and the Compactor), so it's worked out
        // again only when your Garden level or equipment changes.
        int signature = RecipeSignature();
        if (_availableRecipes is null || signature != _availableSignature)
        {
            _availableRecipes = UpgradeTargets
                .Select(t => NextRecipe(t.Kind, t.Target))
                .OfType<Recipe>()
                .Where(r => CraftBlocker(r) is null)
                .ToList();
            _availableSignature = signature;
        }
        return _availableRecipes;
    }

    private IReadOnlyList<Recipe>? _availableRecipes;
    private int _availableSignature;

    private int RecipeSignature()
    {
        var hash = new HashCode();
        hash.Add(GardenLevel);
        hash.Add(State.Compactor);
        foreach (var (id, tool) in State.Tools)
        {
            hash.Add(id);
            hash.Add(tool.Tier);
        }
        foreach (var (slot, tier) in State.Armour)
        {
            hash.Add(slot);
            hash.Add(tier);
        }
        foreach (var (family, tier) in State.Talismans)
        {
            hash.Add(family);
            hash.Add(tier);
        }
        return hash.ToHashCode();
    }

    /// <summary>Upgrades you can craft right now.</summary>
    public IEnumerable<Recipe> ReadyRecipes() => AvailableNextRecipes().Where(r => CanCraft(r, out _));

    // ================================================================= keeping crops for upgrades

    /// <summary>What your next upgrades need of one crop, counting the crops you'd have to pack.</summary>
    public CropNeeds UpgradeNeeds(CropDef crop)
    {
        long raw = 0, packed = 0, dense = 0;
        foreach (var recipe in AvailableNextRecipes())
        foreach (var ingredient in recipe.Ingredients)
        {
            var item = ingredient.Item;
            if (item.Crop != crop) continue;
            switch (item.Kind)
            {
                case ItemKind.Crop: raw += ingredient.Count; break;
                case ItemKind.Packed: packed += ingredient.Count; break;
                default: dense += ingredient.Count; break;
            }
        }

        long denseShort = Math.Max(0, dense - GetItem(Items.Dense(crop)));
        packed += denseShort * ItemDef.PackSize;
        long packedShort = Math.Max(0, packed - GetItem(Items.Packed(crop)));
        raw += packedShort * ItemDef.PackSize;
        return new CropNeeds(raw, packed, dense);
    }

    /// <summary>How many of an item are kept back from the Market for your next upgrades (0 if that setting is off).</summary>
    public long KeptForUpgrades(ItemDef item)
    {
        if (!State.Settings.KeepForUpgrades) return 0;
        var needs = UpgradeNeeds(item.Crop);
        long need = item.Kind switch
        {
            ItemKind.Crop => needs.Raw,
            ItemKind.Packed => needs.Packed,
            _ => needs.Dense,
        };
        return Math.Min(need, GetItem(item));
    }

    /// <summary>How many of an item the Market buttons (and auto-sell) are allowed to sell.</summary>
    public long SellableAmount(ItemDef item) =>
        IsLocked(item) ? 0 : Math.Max(0, GetItem(item) - KeptForUpgrades(item));

    // ================================================================= locks & auto-sell

    public bool IsLocked(ItemDef item) => State.Settings.LockedItems.Contains(item.Id);

    /// <summary>Locked items are never sold: not to the Market, by auto-sell, or on the Bazaar.</summary>
    public void SetLocked(ItemDef item, bool locked)
    {
        if (locked) State.Settings.LockedItems.Add(item.Id);
        else State.Settings.LockedItems.Remove(item.Id);
        RaiseChanged();
    }

    public bool IsAutoSelling(CropDef crop) => State.Settings.AutoSellCrops.Contains(crop.Id);

    /// <summary>Sell this crop to the Market as soon as it's harvested.</summary>
    public void SetAutoSell(CropDef crop, bool autoSell)
    {
        if (autoSell) State.Settings.AutoSellCrops.Add(crop.Id);
        else State.Settings.AutoSellCrops.Remove(crop.Id);
        RaiseChanged();
    }

    // ================================================================= the Compactor

    /// <summary>True if you own a Compactor and it isn't turned off for this crop.</summary>
    public bool IsCompacting(CropDef crop) =>
        State.Compactor > 0 && !State.Settings.CompactorSkips.Contains(crop.Id);

    public void SetCompacting(CropDef crop, bool compact)
    {
        if (compact) State.Settings.CompactorSkips.Remove(crop.Id);
        else State.Settings.CompactorSkips.Add(crop.Id);
        if (compact) Compact(crop);
        RaiseChanged();
    }

    /// <summary>Lets the Compactor pack everything it's allowed to (after crafting it, for example).</summary>
    public void CompactEverything()
    {
        foreach (var crop in Crops.All)
            if (IsCompacting(crop)) Compact(crop);
    }

    /// <summary>
    /// Packs a crop as far as your Compactor can. Plain crops your next upgrades need as they are
    /// (like the 48 Wheat for a first hoe) are left alone if Keep for upgrades is on.
    /// </summary>
    private void Compact(CropDef crop)
    {
        PackSpare(Items.Raw(crop));
        if (State.Compactor >= 2) PackSpare(Items.Packed(crop));
    }

    private void PackSpare(ItemDef item)
    {
        long keep = State.Settings.KeepForUpgrades ? DirectNeed(item) : 0;
        long made = Math.Max(0, GetItem(item) - keep) / ItemDef.PackSize;
        if (made <= 0) return;
        AddItem(item, -made * ItemDef.PackSize);
        AddItem(item.PacksInto!, made);
    }

    /// <summary>How many of exactly this item your next upgrades use as an ingredient.</summary>
    private long DirectNeed(ItemDef item)
    {
        long need = 0;
        foreach (var recipe in AvailableNextRecipes())
        foreach (var ingredient in recipe.Ingredients)
            if (ingredient.ItemId == item.Id) need += ingredient.Count;
        return need;
    }

    // ================================================================= buying missing ingredients

    /// <summary>Ingredients of a recipe you don't have enough of, and how many more you need.</summary>
    public IReadOnlyList<Ingredient> MissingIngredients(Recipe recipe) => MissingItems(recipe.Ingredients);

    /// <summary>What buying the missing ingredients on the Bazaar would cost right now.</summary>
    public long MissingIngredientsCost(Recipe recipe) => BazaarCost(MissingIngredients(recipe));

    /// <summary>Of a list of items, the ones you don't have enough of, and how many more you need.</summary>
    public IReadOnlyList<Ingredient> MissingItems(IEnumerable<Ingredient> wanted) =>
        wanted
            .Where(i => GetItem(i.ItemId) < i.Count)
            .Select(i => new Ingredient(i.ItemId, i.Count - GetItem(i.ItemId)))
            .ToList();

    /// <summary>What buying these items on the Bazaar would cost right now.</summary>
    public long BazaarCost(IEnumerable<Ingredient> items) =>
        items.Sum(i => (long)Math.Ceiling(i.Count * BazaarBuyPrice(i.Item)));

    /// <summary>Buys what a visitor asked for that you don't have, on the Bazaar. Returns coins spent.</summary>
    public long BuyMissingForVisitor(long visitorId, out string reason)
    {
        var request = State.Garden.Visitors.FirstOrDefault(v => v.Id == visitorId);
        if (request is null)
        {
            reason = "That visitor has gone";
            return 0;
        }
        var missing = MissingItems(request.Wants);
        if (missing.Count == 0)
        {
            reason = "You already have everything";
            return 0;
        }
        long cost = BazaarCost(missing);
        if (State.Coins < cost)
        {
            reason = $"Needs {cost - State.Coins:N0} more coins";
            return 0;
        }
        long spent = 0;
        foreach (var item in missing) spent += BazaarBuy(item.ItemId, item.Count, out _);
        reason = "";
        return spent;
    }

    /// <summary>Buys whatever a recipe is missing on the Bazaar, at its buy price. Returns coins spent.</summary>
    public long BuyMissingIngredients(Recipe recipe, out string reason)
    {
        if (CraftBlocker(recipe) is { } blocker)
        {
            reason = blocker;
            return 0;
        }
        var missing = MissingIngredients(recipe);
        if (missing.Count == 0)
        {
            reason = "You already have everything";
            return 0;
        }
        long cost = MissingIngredientsCost(recipe);
        if (State.Coins < cost)
        {
            reason = $"Needs {cost - State.Coins:N0} more coins";
            return 0;
        }

        long spent = 0;
        foreach (var ingredient in missing)
            spent += BazaarBuy(ingredient.ItemId, ingredient.Count, out _);
        reason = "";
        return spent;
    }

    // ================================================================= which crop pays best

    /// <summary>Crops broken per second while the farmer works the row non-stop.</summary>
    public double BreaksPerSecond(CropDef crop) => FieldLength / Math.Max(LapSeconds, crop.RegrowSeconds);

    /// <summary>
    /// Roughly how many coins an hour the farmer would make farming this crop and selling it to the
    /// Market, with your hoe, Fortune and speed.
    /// </summary>
    public double CoinsPerHour(CropDef crop) =>
        BreaksPerSecond(crop) * 3600 * ExpectedDrops(crop, handBreak: false) * MarketPrice(Items.Raw(crop));

    /// <summary>The unlocked crop that makes the most coins an hour.</summary>
    public CropDef BestCropForCoins => UnlockedCrops.MaxBy(CoinsPerHour) ?? Crops.Wheat;
}
