using System;
using System.Linq;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>Plain-English descriptions of equipment and recipes, shared by the strip and the panel.</summary>
internal static class Describe
{
    /// <summary>What an upgrade does once crafted, e.g. "+30 Wheat Fortune".</summary>
    public static string Effect(GameEngine engine, Recipe recipe) => Effect(recipe.Kind, recipe.Target, recipe.Tier);

    public static string Effect(UpgradeKind kind, string target, int tier)
    {
        if (tier <= 0) return "Nothing yet";
        return kind switch
        {
            UpgradeKind.Tool =>
                $"+{Tools.Fortune(tier)} {Crops.Get(target).Name} Fortune. The farmer uses it whenever {Crops.Get(target).Name} is planted.",
            UpgradeKind.Armour =>
                $"+{Armour.Fortune(tier)} Harvest Fortune. Full {Armour.SetName(tier)} set: {Armour.SetBonus(tier).Describe()}.",
            UpgradeKind.Compactor => Compactors.Effect(tier) + ". Turn it off for any crop in the Inventory tab.",
            _ => Talismans.Bonus(Enum.Parse<TalismanFamily>(target), tier).Describe() + ".",
        };
    }

    /// <summary>"5 Packed Wheat, 2 Dense Melon".</summary>
    public static string Items(System.Collections.Generic.IEnumerable<Ingredient> ingredients) =>
        string.Join(", ", ingredients.Select(i => $"{Format.Number(i.Count)} {i.Item.Name}"));

    /// <summary>"8 Packed Wheat (you have 3) · 5,000 coins · Garden Level 4".</summary>
    public static string Cost(GameEngine engine, Recipe recipe)
    {
        var parts = recipe.Ingredients
            .Select(i => $"{Format.Number(i.Count)} {i.Item.Name} (you have {Format.Number(engine.GetItem(i.ItemId))})")
            .ToList();
        parts.Add($"{Format.Number(recipe.Coins)} coins");
        if (recipe.GardenLevel > 1) parts.Add($"Garden Level {recipe.GardenLevel}");
        return string.Join(" · ", parts);
    }
}
