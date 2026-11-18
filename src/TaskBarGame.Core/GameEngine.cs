namespace TaskBarGame.Core;

public sealed record BreakResult(
    int TileIndex,
    CropDef Crop,
    long Drops,
    double FarmingXp,
    long GardenXp,
    bool HandBreak,
    long CoinsFromAutoSell);

public enum SkillKind { Farming, Garden, Fishing }

/// <param name="Rewards">Lines describing what the new level gave, for popups and the panel.</param>
public sealed record LevelUp(SkillKind Skill, int OldLevel, int NewLevel, IReadOnlyList<string> Rewards);

public sealed record CollectionTierUp(CropDef Crop, int NewTier);

/// <summary>What happened while the game was closed.</summary>
public sealed class OfflineReport
{
    public TimeSpan Away { get; init; }
    public TimeSpan Counted { get; set; }
    public long Breaks { get; set; }
    public long Drops { get; set; }
    public double FarmingXp { get; set; }
    public long Coins { get; set; }
    public int FarmingLevelBefore { get; set; }
    public int FarmingLevelAfter { get; set; }
    public int GardenLevelBefore { get; set; }
    public int GardenLevelAfter { get; set; }
    public int VisitorsArrived { get; set; }
    public long CompostMade { get; set; }
    public long RareFinds { get; set; }

    /// <summary>Catches reeled in while away (if the farmer was left fishing).</summary>
    public long Catches { get; set; }

    /// <summary>Items those catches gave (double catches count twice).</summary>
    public long FishCaught { get; set; }
    public double FishingXp { get; set; }
    public long Treasures { get; set; }
    public int FishingLevelBefore { get; set; }
    public int FishingLevelAfter { get; set; }

    /// <summary>What those catches were, by item id.</summary>
    public Dictionary<string, long> Caught { get; } = new();

    /// <summary>The bait that ran out while away, or null.</summary>
    public string? BaitRanOut { get; set; }

    public bool IsEmpty => Breaks == 0 && Catches == 0;
}

/// <summary>One line of a stat breakdown, e.g. ("Farming Level 12", 48).</summary>
public sealed record StatSource(string Source, int Amount);

/// <summary>
/// All the game rules: the field, breaking crops, XP, levels, collections, packing,
/// crafting upgrades and selling. The WPF app only draws things and calls into here,
/// so these rules can be unit tested.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>Extra Harvest Fortune when you click a grown crop yourself.</summary>
    public const int HandBreakFortuneBonus = 25;

    /// <summary>Clicking a growing crop ("tending") skips this fraction of its regrow time.</summary>
    public const double TendFraction = 0.15;

    /// <summary>The farmer's walking speed with no bonuses, in crops per second.</summary>
    public const double BaseFarmerSpeed = 2.5;

    /// <summary>The farmer keeps working while the game is closed, up to this long.</summary>
    public static readonly TimeSpan MaxOfflineTime = TimeSpan.FromHours(12);

    /// <summary>While the game is closed the farmer works at this speed (0.5 = half speed).</summary>
    public const double OfflineEfficiency = 0.5;

    private readonly Random _rng;
    private bool _quiet;

    public GameEngine(GameState state, Random? rng = null)
    {
        State = state;
        State.Normalize();
        _rng = rng ?? new Random();
        EnsureBazaarPrices();
    }

    public GameState State { get; }

    /// <summary>A crop was broken (by the farmer, by you, or while away).</summary>
    public event Action<BreakResult>? Broke;
    public event Action<LevelUp>? LeveledUp;
    public event Action<CollectionTierUp>? CollectionTierReached;
    public event Action<Recipe>? Crafted;

    /// <summary>The field, inventory, equipment or settings changed (not raised for plain growth).</summary>
    public event Action? Changed;

    // ================================================================= levels & crops

    public int FarmingLevel => FarmingSkill.LevelForXp(State.FarmingXp);
    public int GardenLevel => GardenProgression.LevelForXp(State.GardenXp);

    public bool IsCropUnlocked(CropDef crop) => GardenLevel >= crop.UnlockGardenLevel;
    public IEnumerable<CropDef> UnlockedCrops => Crops.All.Where(IsCropUnlocked);

    public long GetCollection(CropDef crop) => State.Collections.GetValueOrDefault(crop.Id);
    public int GetCollectionTier(CropDef crop) => CollectionTiers.TierFor(crop, GetCollection(crop));

    // ================================================================= the field

    public CropDef FieldCrop => Crops.Get(State.Field.CropId);
    public int FieldLength => State.Field.Length;
    public IReadOnlyList<double> Tiles => State.Field.Tiles;

    public bool IsGrown(int tile) =>
        tile >= 0 && tile < Tiles.Count && Tiles[tile] >= FieldCrop.RegrowSeconds;

    /// <summary>0..1 how grown a crop in the row is.</summary>
    public double GrowthProgress(int tile) =>
        tile >= 0 && tile < Tiles.Count ? Math.Clamp(Tiles[tile] / FieldCrop.RegrowSeconds, 0, 1) : 0;

    /// <summary>0 = just broken, 1 = sprouting, 2 = growing, 3 = grown.</summary>
    public int GrowthStage(int tile)
    {
        double p = GrowthProgress(tile);
        return p >= 1 ? 3 : p >= 0.6 ? 2 : p >= 0.25 ? 1 : 0;
    }

    public int GrownCount => Enumerable.Range(0, Tiles.Count).Count(IsGrown);

    /// <summary>How fast the farmer walks the row, in crops per second.</summary>
    public double FarmerSpeed => BaseFarmerSpeed * (1 + SpeedPercent / 100.0);

    /// <summary>
    /// Seconds for one lap: walking from the start pad, past every crop, onto the end pad, and
    /// teleporting back to the start.
    /// </summary>
    public double LapSeconds => (FieldLength + 1) / FarmerSpeed + FarmerAgent.TeleportDuration;

    // ================================================================= equipment & stats

    public ToolState GetTool(CropDef crop)
    {
        if (!State.Tools.TryGetValue(crop.Id, out var tool))
            State.Tools[crop.Id] = tool = new ToolState();
        return tool;
    }

    /// <summary>The tool the farmer is holding: always the one for the planted crop.</summary>
    public ToolState ActiveTool => GetTool(FieldCrop);

    public int GetArmourTier(ArmourSlot slot) => State.Armour.GetValueOrDefault(slot);
    public int GetTalismanTier(TalismanFamily family) => State.Talismans.GetValueOrDefault(family);

    /// <summary>The best armour set you're wearing all four pieces of (0 = none).</summary>
    public int FullSetTier => Armour.Slots.Min(GetArmourTier);

    /// <summary>Bonuses from armour (pieces and set bonus) and talismans, which apply to every crop.</summary>
    public StatBonus EquipmentBonus
    {
        get
        {
            var bonus = new StatBonus(Fortune: Armour.Slots.Sum(s => Armour.Fortune(GetArmourTier(s))));
            bonus += Armour.SetBonus(FullSetTier);
            foreach (var family in Talismans.Families)
                bonus += Talismans.Bonus(family, GetTalismanTier(family));
            return bonus;
        }
    }

    /// <summary>Equipment plus the garden (Composter upgrades and running boosts).</summary>
    public StatBonus TotalBonus => EquipmentBonus + GardenBonus + RareFindsBonus;

    public int SpeedPercent => TotalBonus.Speed;
    public int SellPercent => TotalBonus.Sell;
    public int XpPercent => TotalBonus.Xp;

    /// <summary>What the Market pays for one of an item, with your Market sell price bonus.</summary>
    public double MarketPrice(ItemDef item) => item.SellPrice * (1 + SellPercent / 100.0);

    /// <summary>
    /// Harvest Fortune for a crop: every 100 adds one extra set of drops per break
    /// (150 fortune = 2.5x drops; the leftover half is a 50% chance).
    /// </summary>
    public int GetFortune(CropDef crop, bool handBreak) => FortuneSources(crop, handBreak).Sum(s => s.Amount);

    /// <summary>Where a crop's Harvest Fortune comes from, for the Farming tab.</summary>
    public IReadOnlyList<StatSource> FortuneSources(CropDef crop, bool handBreak = false)
    {
        var tool = GetTool(crop);
        var sources = new List<StatSource>
        {
            new($"Farming Level {FarmingLevel}", FarmingLevel * FarmingSkill.FortunePerLevel),
            new($"{crop.Name} collection", GetCollectionTier(crop) * CollectionTiers.FortunePerTier),
            new(Tools.Name(crop, tool.Tier), Tools.Fortune(tool.Tier)),
            new($"{Tools.ToolName(crop)} counter ({tool.Breaks:N0} broken)", Tools.CounterFortune(tool.Breaks)),
        };
        foreach (var slot in Armour.Slots)
            sources.Add(new StatSource(Armour.Name(slot, GetArmourTier(slot)), Armour.Fortune(GetArmourTier(slot))));
        sources.Add(new StatSource($"{Armour.SetName(FullSetTier)} set bonus", Armour.SetBonus(FullSetTier).Fortune));
        sources.Add(new StatSource(Talismans.Name(TalismanFamily.Clover, GetTalismanTier(TalismanFamily.Clover)),
            Talismans.Bonus(TalismanFamily.Clover, GetTalismanTier(TalismanFamily.Clover)).Fortune));
        foreach (var upgrade in Garden.Upgrades.Where(u => u.PerLevel.Fortune != 0 && UpgradeLevel(u) > 0))
            sources.Add(new StatSource($"{upgrade.Name} {UpgradeLevel(upgrade)} (Composter)", upgrade.PerLevel.Fortune * UpgradeLevel(upgrade)));
        foreach (var (boost, left) in ActiveBoosts.Where(b => b.Boost.Bonus.Fortune != 0))
            sources.Add(new StatSource($"{boost.Name} ({Math.Ceiling(left / 60):0}m left)", boost.Bonus.Fortune));
        foreach (var find in RareFinds.All.Where(f => f.Bonus.Fortune != 0 && RareFind(f).Found > 0))
            sources.Add(new StatSource($"{find.Name} (rare find)", find.Bonus.Fortune));
        if (handBreak) sources.Add(new StatSource("Breaking it yourself", HandBreakFortuneBonus));
        return sources.Where(s => s.Amount != 0).ToList();
    }

    public double ExpectedDrops(CropDef crop, bool handBreak) =>
        crop.DropsPerBreak * (1 + GetFortune(crop, handBreak) / 100.0);

    // ================================================================= growing & breaking

    /// <summary>Grows every crop in the row and moves Bazaar prices on by <paramref name="seconds"/>.</summary>
    public void Tick(double seconds)
    {
        if (seconds <= 0 || !double.IsFinite(seconds)) return;
        GrowCrops(seconds);
        BazaarTick(seconds);
        VisitorTick(seconds);
        CompostTick(seconds);
        BoostTick(seconds);
    }

    private void GrowCrops(double seconds)
    {
        double full = FieldCrop.RegrowSeconds;
        var tiles = State.Field.Tiles;
        for (int i = 0; i < tiles.Count; i++)
            if (tiles[i] < full) tiles[i] = Math.Min(full, tiles[i] + seconds);
    }

    /// <summary>Breaks a grown crop in the row. It starts growing back straight away.</summary>
    public BreakResult? Break(int tile, bool handBreak)
    {
        if (!IsGrown(tile)) return null;
        State.Field.Tiles[tile] = 0;
        return BreakInternal(tile, handBreak);
    }

    /// <summary>Clicking a growing crop speeds it up a little. Returns false if nothing happened.</summary>
    public bool Tend(int tile)
    {
        if (tile < 0 || tile >= Tiles.Count || IsGrown(tile)) return false;
        double full = FieldCrop.RegrowSeconds;
        State.Field.Tiles[tile] = Math.Min(full, Tiles[tile] + full * TendFraction);
        return true;
    }

    /// <summary>Replants the whole row with another crop (starting from seeds).</summary>
    public bool PlantField(string cropId)
    {
        if (!Crops.TryGet(cropId, out var crop) || !IsCropUnlocked(crop)) return false;
        if (crop.Id == State.Field.CropId) return true;

        State.Field.CropId = crop.Id;
        for (int i = 0; i < State.Field.Tiles.Count; i++) State.Field.Tiles[i] = 0;
        RaiseChanged();
        return true;
    }

    public bool CanExpandField(out FieldExpansion? next, out string reason)
    {
        next = FieldProgression.Next(FieldLength);
        if (next is null)
        {
            reason = "Your field is as long as it gets";
            return false;
        }
        if (GardenLevel < next.GardenLevel)
        {
            reason = $"Needs Garden Level {next.GardenLevel}";
            return false;
        }
        if (State.Coins < next.Coins)
        {
            reason = $"Needs {next.Coins:N0} coins";
            return false;
        }
        reason = "";
        return true;
    }

    public bool TryExpandField(out string reason)
    {
        if (!CanExpandField(out var next, out reason)) return false;
        State.Coins -= next!.Coins;
        State.Field.Length = next.Length;
        while (State.Field.Tiles.Count < next.Length) State.Field.Tiles.Add(0);
        RaiseChanged();
        return true;
    }

    // ================================================================= inventory, packing & selling

    public long GetItem(ItemDef item) => State.Inventory.GetValueOrDefault(item.Id);
    public long GetItem(string itemId) => State.Inventory.GetValueOrDefault(itemId);

    private void AddItem(ItemDef item, long count)
    {
        long total = GetItem(item) + count;
        if (total > 0) State.Inventory[item.Id] = total;
        else State.Inventory.Remove(item.Id);
    }

    /// <summary>
    /// Packs an item into the next tier (160 crops → 1 packed, 160 packed → 1 dense): as many as
    /// possible, or at most <paramref name="maxMade"/>. Returns how many were made.
    /// </summary>
    public long Pack(string itemId, long maxMade = long.MaxValue)
    {
        if (!Items.TryGet(itemId, out var item) || item.PacksInto is not { } result || maxMade <= 0) return 0;
        long made = Math.Min(maxMade, GetItem(item) / ItemDef.PackSize);
        if (made <= 0) return 0;
        AddItem(item, -made * ItemDef.PackSize);
        AddItem(result, made);
        RaiseChanged();
        return made;
    }

    /// <summary>
    /// Sells up to <paramref name="amount"/> of an item to the Market. Locked items, and what your
    /// next upgrades need (if <see cref="GameSettings.KeepForUpgrades"/> is on), are kept.
    /// Returns coins earned.
    /// </summary>
    public long Sell(string itemId, long amount)
    {
        if (!Items.TryGet(itemId, out var item) || amount <= 0) return 0;
        long sold = Math.Min(SellableAmount(item), amount);
        if (sold <= 0) return 0;

        long coins = SellToMarket(item, sold);
        RaiseChanged();
        return coins;
    }

    private long SellToMarket(ItemDef item, long amount)
    {
        long coins = (long)Math.Floor(amount * MarketPrice(item));
        AddItem(item, -amount);
        State.Coins += coins;
        State.Stats.CoinsEarned += coins;
        return coins;
    }

    /// <summary>Sells every plain crop that isn't locked or kept for upgrades (packed and dense crops are kept for crafting).</summary>
    public long SellAllCrops()
    {
        long total = 0;
        bool quiet = _quiet;
        _quiet = true;
        try
        {
            foreach (var crop in Crops.All)
                total += Sell(crop.Id, long.MaxValue);
        }
        finally
        {
            _quiet = quiet;
        }
        if (total > 0) RaiseChanged();
        return total;
    }

    /// <summary>What the Market would pay for your plain crops (the ones "Sell all crops" would sell).</summary>
    public long CropsValue() => (long)Math.Floor(Crops.All.Sum(c => SellableAmount(Items.Raw(c)) * MarketPrice(Items.Raw(c))));

    // ================================================================= crafting

    public int GetTier(UpgradeKind kind, string target) => kind switch
    {
        UpgradeKind.Tool => Crops.TryGet(target, out var crop) ? GetTool(crop).Tier : 0,
        UpgradeKind.Armour => GetArmourTier(Enum.Parse<ArmourSlot>(target)),
        UpgradeKind.Compactor => State.Compactor,
        UpgradeKind.Rod => State.Fishing.Rod,
        UpgradeKind.FishingArmour => GetFishingArmourTier(Enum.Parse<ArmourSlot>(target)),
        _ => GetTalismanTier(Enum.Parse<TalismanFamily>(target)),
    };

    /// <summary>The next upgrade for a tool, armour slot or talisman, or null if it's maxed.</summary>
    public Recipe? NextRecipe(UpgradeKind kind, string target) => Recipes.Find(kind, target, GetTier(kind, target) + 1);

    /// <summary>
    /// Why a recipe can't be crafted yet, not counting its ingredients and coins
    /// (the wrong tier, or a Garden level you haven't reached). Null if nothing's in the way.
    /// </summary>
    public string? CraftBlocker(Recipe recipe)
    {
        int tier = GetTier(recipe.Kind, recipe.Target);
        if (tier >= recipe.Tier) return "Already owned";
        if (tier != recipe.Tier - 1) return $"Needs the tier {recipe.Tier - 1} version first";
        if (recipe.Kind == UpgradeKind.Tool && !IsCropUnlocked(Crops.Get(recipe.Target)))
            return $"Needs Garden Level {Crops.Get(recipe.Target).UnlockGardenLevel}";
        if (GardenLevel < recipe.GardenLevel) return $"Needs Garden Level {recipe.GardenLevel}";
        if (FishingLevel < recipe.FishingLevel) return $"Needs Fishing Level {recipe.FishingLevel}";
        return null;
    }

    public bool CanCraft(Recipe recipe, out string reason)
    {
        if (CraftBlocker(recipe) is { } blocker)
        {
            reason = blocker;
            return false;
        }
        foreach (var ingredient in recipe.Ingredients)
        {
            long have = GetItem(ingredient.ItemId);
            if (have < ingredient.Count)
            {
                reason = $"Needs {ingredient.Count - have:N0} more {ingredient.Item.Name}";
                return false;
            }
        }
        if (State.Coins < recipe.Coins)
        {
            reason = $"Needs {recipe.Coins - State.Coins:N0} more coins";
            return false;
        }
        reason = "";
        return true;
    }

    public bool TryCraft(Recipe recipe, out string reason)
    {
        if (!CanCraft(recipe, out reason)) return false;

        foreach (var ingredient in recipe.Ingredients)
            AddItem(ingredient.Item, -ingredient.Count);
        State.Coins -= recipe.Coins;

        switch (recipe.Kind)
        {
            case UpgradeKind.Tool:
                GetTool(Crops.Get(recipe.Target)).Tier = recipe.Tier;
                break;
            case UpgradeKind.Armour:
                State.Armour[Enum.Parse<ArmourSlot>(recipe.Target)] = recipe.Tier;
                break;
            case UpgradeKind.Talisman:
                State.Talismans[Enum.Parse<TalismanFamily>(recipe.Target)] = recipe.Tier;
                break;
            case UpgradeKind.Compactor:
                State.Compactor = recipe.Tier;
                break;
            case UpgradeKind.Rod:
                State.Fishing.Rod = recipe.Tier;
                break;
            case UpgradeKind.FishingArmour:
                State.Fishing.Armour[Enum.Parse<ArmourSlot>(recipe.Target)] = recipe.Tier;
                break;
        }
        State.Stats.ItemsCrafted++;
        if (recipe.Kind == UpgradeKind.Compactor) CompactEverything();

        if (!_quiet) Crafted?.Invoke(recipe);
        RaiseChanged();
        return true;
    }

    // ================================================================= offline time

    /// <summary>
    /// Lets the farmer catch up on work done while the game was closed (or the PC was asleep).
    /// Capped at <see cref="MaxOfflineTime"/> and done at <see cref="OfflineEfficiency"/> speed.
    /// </summary>
    public OfflineReport ApplyOfflineProgress(TimeSpan away)
    {
        var report = new OfflineReport
        {
            Away = away,
            FarmingLevelBefore = FarmingLevel,
            GardenLevelBefore = GardenLevel,
            FishingLevelBefore = FishingLevel,
        };

        double seconds = Math.Min(away.TotalSeconds, MaxOfflineTime.TotalSeconds);
        if (seconds > 0)
        {
            report.Counted = TimeSpan.FromSeconds(seconds);
            long compostBefore = State.Garden.Compost;
            long rareBefore = State.RareFinds.Values.Sum(f => f.Found);
            _quiet = true;
            try
            {
                BazaarTick(seconds);
                report.VisitorsArrived = VisitorTick(seconds);
                CompostTick(seconds);
            }
            finally { _quiet = false; }
            report.CompostMade = State.Garden.Compost - compostBefore;

            if (CurrentLake is { } lake)
            {
                // Away fishing: the field just grows.
                _quiet = true;
                try { FishOffline(lake, seconds, report); }
                finally { _quiet = false; }
                BoostTick(seconds);
                GrowCrops(seconds);
            }
            else if (!State.Settings.FarmerAutoHarvest)
            {
                BoostTick(seconds);
                GrowCrops(seconds);
            }
            else
            {
                long coinsBefore = State.Coins;
                double xpBefore = State.FarmingXp;

                // Boosts run out part-way through: farm in stretches, each with the boosts running then.
                double remaining = seconds, owed = 0;
                _quiet = true;
                try
                {
                    while (remaining > 0)
                    {
                        double stretch = remaining;
                        foreach (var (_, left) in ActiveBoosts) stretch = Math.Min(stretch, left);
                        owed += stretch * OfflineBreaksPerSecond();
                        long breaks = (long)Math.Floor(owed);
                        owed -= breaks;
                        for (long i = 0; i < breaks; i++)
                            report.Drops += BreakInternal(-1, handBreak: false).Drops;
                        report.Breaks += breaks;
                        BoostTick(stretch);
                        remaining -= stretch;
                    }
                }
                finally
                {
                    _quiet = false;
                }

                report.Coins = State.Coins - coinsBefore;
                report.FarmingXp = State.FarmingXp - xpBefore;
                GrowCrops(seconds); // the row is grown again when you come back
            }
            report.RareFinds = State.RareFinds.Values.Sum(f => f.Found) - rareBefore;
        }

        report.FarmingLevelAfter = FarmingLevel;
        report.GardenLevelAfter = GardenLevel;
        report.FishingLevelAfter = FishingLevel;
        if (!report.IsEmpty || report.VisitorsArrived > 0 || report.CompostMade > 0) RaiseChanged();
        return report;
    }

    /// <summary>
    /// While away the farmer breaks the whole row once per lap (or once per regrow, if crops were
    /// ever slower than a lap), at <see cref="OfflineEfficiency"/>.
    /// </summary>
    public double OfflineBreaksPerSecond() =>
        FieldLength / Math.Max(LapSeconds, FieldCrop.RegrowSeconds) * OfflineEfficiency;

    /// <summary>Starts over: wipes coins, XP, items and equipment but keeps your settings.</summary>
    public void ResetProgress()
    {
        var settings = State.Settings;
        var fresh = GameState.CreateNew();
        State.Coins = 0;
        State.FarmingXp = 0;
        State.GardenXp = 0;
        State.Inventory.Clear();
        State.Collections.Clear();
        State.Field = fresh.Field;
        State.Tools.Clear();
        State.Armour.Clear();
        State.Talismans.Clear();
        State.Compactor = 0;
        State.UnlockedCosmetics.Clear();
        State.Garden = new GardenState();
        State.RareFinds.Clear();
        State.Fishing = new FishingState();
        State.Bazaar = new BazaarState();
        EnsureBazaarPrices();
        State.Stats = new GameStats();
        State.Settings = settings;
        RaiseChanged();
    }

    // ================================================================= internals

    private BreakResult BreakInternal(int tile, bool handBreak)
    {
        var crop = FieldCrop;
        int farmingBefore = FarmingLevel;
        int gardenBefore = GardenLevel;
        int tierBefore = GetCollectionTier(crop);

        long drops = RollDrops(crop, handBreak);
        State.Collections[crop.Id] = GetCollection(crop) + drops;

        // Into the inventory, then auto-sell (what isn't locked or kept for upgrades), then the Compactor.
        var raw = Items.Raw(crop);
        AddItem(raw, drops);
        long autoSellCoins = 0;
        if (IsAutoSelling(crop))
        {
            long sell = Math.Min(drops, SellableAmount(raw));
            if (sell > 0) autoSellCoins = SellToMarket(raw, sell);
        }
        if (IsCompacting(crop)) Compact(crop);

        var tool = GetTool(crop);
        if (tool.Tier > 0) tool.Breaks++;
        RollRareFind(tile);

        int tierAfter = GetCollectionTier(crop);
        long gardenXp = GardenProgression.XpPerBreak;
        for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
            gardenXp += (long)CollectionTiers.GardenXpPerTier * tier;

        double farmingXp = crop.FarmingXp * (1 + XpPercent / 100.0);
        State.FarmingXp += farmingXp;
        State.GardenXp += gardenXp;

        State.Stats.TotalBreaks++;
        if (handBreak) State.Stats.HandBreaks++;

        var result = new BreakResult(tile, crop, drops, farmingXp, gardenXp, handBreak, autoSellCoins);
        if (!_quiet) Broke?.Invoke(result);

        for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
            if (!_quiet) CollectionTierReached?.Invoke(new CollectionTierUp(crop, tier));

        if (FarmingLevel > farmingBefore) OnFarmingLevelUp(farmingBefore, FarmingLevel);
        if (GardenLevel > gardenBefore) OnGardenLevelUp(gardenBefore, GardenLevel);
        return result;
    }

    private void OnFarmingLevelUp(int oldLevel, int newLevel)
    {
        long coins = 0;
        for (int level = oldLevel + 1; level <= newLevel; level++)
            coins += (long)level * FarmingSkill.CoinRewardPerLevel;
        State.Coins += coins;

        var rewards = new List<string>
        {
            $"+{(newLevel - oldLevel) * FarmingSkill.FortunePerLevel} Harvest Fortune",
            $"+{coins:N0} coins",
        };
        if (!_quiet) LeveledUp?.Invoke(new LevelUp(SkillKind.Farming, oldLevel, newLevel, rewards));
    }

    private void OnGardenLevelUp(int oldLevel, int newLevel)
    {
        bool NewAt(int level) => level > oldLevel && level <= newLevel;

        var rewards = Crops.All.Where(c => NewAt(c.UnlockGardenLevel)).Select(c => $"Unlocked {c.Name}").ToList();
        if (FieldProgression.Next(FieldLength) is { } next && NewAt(next.GardenLevel))
            rewards.Add($"Field expansion available ({next.Coins:N0} coins)");
        int newRecipes = Recipes.All.Count(r => r.Kind != UpgradeKind.Tool && !r.IsFishing && NewAt(r.GardenLevel));
        if (newRecipes > 0) rewards.Add($"{newRecipes} new upgrade{(newRecipes == 1 ? "" : "s")} to craft");

        if (!_quiet)
        {
            LeveledUp?.Invoke(new LevelUp(SkillKind.Garden, oldLevel, newLevel, rewards));
            RaiseChanged();
        }
    }

    private long RollDrops(CropDef crop, bool handBreak)
    {
        double expected = ExpectedDrops(crop, handBreak);
        long whole = (long)Math.Floor(expected);
        if (_rng.NextDouble() < expected - whole) whole++;
        return whole;
    }

    private void RaiseChanged()
    {
        if (!_quiet) Changed?.Invoke();
    }
}
