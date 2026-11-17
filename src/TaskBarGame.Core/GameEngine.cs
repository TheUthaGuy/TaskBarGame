namespace TaskBarGame.Core;

public sealed record HarvestResult(
    int PlotIndex,
    CropDef Crop,
    long Drops,
    long FarmingXp,
    long GardenXp,
    bool HandHarvest,
    long CoinsFromAutoSell);

public enum SkillKind { Farming, Garden }

/// <param name="Rewards">Lines describing what the new level gave, for toasts and the panel.</param>
public sealed record LevelUp(SkillKind Skill, int OldLevel, int NewLevel, IReadOnlyList<string> Rewards);

public sealed record CollectionTierUp(CropDef Crop, int NewTier);

/// <summary>What happened while the game was closed.</summary>
public sealed class OfflineReport
{
    public TimeSpan Away { get; init; }
    public TimeSpan Counted { get; set; }
    public long Harvests { get; set; }
    public Dictionary<string, long> Drops { get; } = new();
    public long FarmingXp { get; set; }
    public long GardenXp { get; set; }
    public long Coins { get; set; }
    public int FarmingLevelBefore { get; set; }
    public int FarmingLevelAfter { get; set; }
    public int GardenLevelBefore { get; set; }
    public int GardenLevelAfter { get; set; }

    public bool IsEmpty => Harvests == 0;
}

/// <summary>
/// All the game rules: growing, harvesting, XP, levels, collections, plots and selling.
/// The WPF app only draws things and calls into here, so these rules can be unit tested.
/// </summary>
public sealed class GameEngine
{
    /// <summary>Extra Harvest Fortune when you click a ripe crop yourself.</summary>
    public const int HandHarvestFortuneBonus = 25;

    /// <summary>Clicking a growing crop ("tending") skips this fraction of its grow time.</summary>
    public const double TendFraction = 0.04;

    /// <summary>The farmer keeps working while the game is closed, up to this long.</summary>
    public static readonly TimeSpan MaxOfflineTime = TimeSpan.FromHours(12);

    /// <summary>While the game is closed the farmer works at this speed (0.5 = half speed).</summary>
    public const double OfflineEfficiency = 0.5;

    /// <summary>Seconds the farmer spends walking between harvests, used for offline time.</summary>
    public const double OfflineWalkSeconds = 3;

    private readonly Random _rng;
    private bool _quiet;

    public GameEngine(GameState state, Random? rng = null)
    {
        State = state;
        State.Normalize();
        _rng = rng ?? new Random();
    }

    public GameState State { get; }

    public event Action<HarvestResult>? Harvested;
    public event Action<LevelUp>? LeveledUp;
    public event Action<CollectionTierUp>? CollectionTierReached;

    /// <summary>Plots, crops, inventory or settings changed (not raised for plain growth).</summary>
    public event Action? Changed;

    // ---------------------------------------------------------------- queries

    public int FarmingLevel => FarmingSkill.LevelForXp(State.FarmingXp);
    public int GardenLevel => GardenProgression.LevelForXp(State.GardenXp);
    public IReadOnlyList<PlotState> Plots => State.Plots;

    public bool IsCropUnlocked(CropDef crop) => GardenLevel >= crop.UnlockGardenLevel;
    public IEnumerable<CropDef> UnlockedCrops => Crops.All.Where(IsCropUnlocked);

    public long GetCollection(CropDef crop) => State.Collections.GetValueOrDefault(crop.Id);
    public int GetCollectionTier(CropDef crop) => CollectionTiers.TierFor(crop, GetCollection(crop));
    public long GetInventory(CropDef crop) => State.Inventory.GetValueOrDefault(crop.Id);

    public long InventoryValue() =>
        Crops.All.Sum(c => GetInventory(c) * c.SellPrice);

    /// <summary>
    /// Harvest Fortune: every 100 adds one extra copy of the base drops
    /// (150 fortune = 2.5x drops; the leftover half is a 50% chance).
    /// </summary>
    public int GetFortune(CropDef crop, bool handHarvest) =>
        FarmingLevel * FarmingSkill.FortunePerLevel
        + GetCollectionTier(crop) * CollectionTiers.FortunePerTier
        + (handHarvest ? HandHarvestFortuneBonus : 0);

    public double ExpectedDrops(CropDef crop, bool handHarvest) =>
        crop.BaseDrops * (1 + GetFortune(crop, handHarvest) / 100.0);

    public static bool IsRipe(PlotState plot) =>
        Crops.TryGet(plot.CropId, out var crop) && plot.Growth >= crop.GrowSeconds;

    /// <summary>0..1 how grown the crop is (0 for empty plots).</summary>
    public static double GrowthProgress(PlotState plot) =>
        Crops.TryGet(plot.CropId, out var crop) ? Math.Clamp(plot.Growth / crop.GrowSeconds, 0, 1) : 0;

    /// <summary>0 = seed, 1 = sprout, 2 = growing, 3 = ripe.</summary>
    public static int GrowthStage(PlotState plot)
    {
        double p = GrowthProgress(plot);
        return p >= 1 ? 3 : p >= 0.6 ? 2 : p >= 0.25 ? 1 : 0;
    }

    public static double SecondsUntilRipe(PlotState plot) =>
        Crops.TryGet(plot.CropId, out var crop) ? Math.Max(0, crop.GrowSeconds - plot.Growth) : 0;

    // ---------------------------------------------------------------- actions

    /// <summary>Grows every planted crop by <paramref name="seconds"/>.</summary>
    public void Tick(double seconds)
    {
        if (seconds <= 0 || !double.IsFinite(seconds)) return;
        foreach (var plot in State.Plots)
        {
            if (Crops.TryGet(plot.CropId, out var crop) && plot.Growth < crop.GrowSeconds)
                plot.Growth = Math.Min(crop.GrowSeconds, plot.Growth + seconds);
        }
    }

    /// <summary>
    /// Harvests a ripe plot and replants the same crop. Returns null if the plot isn't ripe.
    /// </summary>
    public HarvestResult? Harvest(int plotIndex, bool handHarvest)
    {
        if (!TryGetPlot(plotIndex, out var plot) || !IsRipe(plot)) return null;
        return HarvestInternal(plotIndex, plot, Crops.Get(plot.CropId!), handHarvest);
    }

    /// <summary>Clicking a growing crop speeds it up a little. Returns false if nothing happened.</summary>
    public bool Tend(int plotIndex)
    {
        if (!TryGetPlot(plotIndex, out var plot)) return false;
        if (!Crops.TryGet(plot.CropId, out var crop) || plot.Growth >= crop.GrowSeconds) return false;
        plot.Growth = Math.Min(crop.GrowSeconds, plot.Growth + crop.GrowSeconds * TendFraction);
        return true;
    }

    /// <summary>Plants a crop on one plot (restarting its growth). Fails if the crop is locked.</summary>
    public bool Plant(int plotIndex, string cropId)
    {
        if (!TryGetPlot(plotIndex, out var plot)) return false;
        if (!Crops.TryGet(cropId, out var crop) || !IsCropUnlocked(crop)) return false;

        State.LastPlantedCrop = crop.Id;
        if (plot.CropId == crop.Id) return true;

        plot.CropId = crop.Id;
        plot.Growth = 0;
        RaiseChanged();
        return true;
    }

    /// <summary>Plants a crop on every plot that has a different crop. Returns how many changed.</summary>
    public int PlantAll(string cropId)
    {
        if (!Crops.TryGet(cropId, out var crop) || !IsCropUnlocked(crop)) return 0;
        int changed = 0;
        foreach (var plot in State.Plots)
        {
            if (plot.CropId == crop.Id) continue;
            plot.CropId = crop.Id;
            plot.Growth = 0;
            changed++;
        }
        State.LastPlantedCrop = crop.Id;
        if (changed > 0) RaiseChanged();
        return changed;
    }

    /// <summary>Can the next plot be bought right now? <paramref name="reason"/> says why not.</summary>
    public bool CanUnlockNextPlot(out PlotUnlock? cost, out string reason)
    {
        cost = GardenProgression.NextPlotUnlock(State.Plots.Count);
        if (cost is null)
        {
            reason = "All plots unlocked";
            return false;
        }
        if (GardenLevel < cost.GardenLevel)
        {
            reason = $"Needs Garden Level {cost.GardenLevel}";
            return false;
        }
        if (State.Coins < cost.Coins)
        {
            reason = $"Needs {cost.Coins:N0} coins";
            return false;
        }
        reason = "";
        return true;
    }

    public bool TryUnlockNextPlot(out string reason)
    {
        if (!CanUnlockNextPlot(out var cost, out reason)) return false;

        State.Coins -= cost!.Coins;
        string crop = Crops.TryGet(State.LastPlantedCrop, out var last) && IsCropUnlocked(last)
            ? last.Id
            : Crops.Wheat.Id;
        State.Plots.Add(new PlotState { CropId = crop });
        RaiseChanged();
        return true;
    }

    /// <summary>Sells up to <paramref name="amount"/> of a crop. Returns coins earned.</summary>
    public long Sell(string cropId, long amount)
    {
        if (!Crops.TryGet(cropId, out var crop) || amount <= 0) return 0;
        long have = GetInventory(crop);
        long sold = Math.Min(have, amount);
        if (sold <= 0) return 0;

        long coins = sold * crop.SellPrice;
        if (have - sold > 0) State.Inventory[crop.Id] = have - sold;
        else State.Inventory.Remove(crop.Id);

        State.Coins += coins;
        State.Stats.CoinsEarned += coins;
        RaiseChanged();
        return coins;
    }

    public long SellAll()
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

    /// <summary>Starts over: wipes coins, XP, crops and plots but keeps your settings.</summary>
    public void ResetProgress()
    {
        var fresh = GameState.CreateNew();
        State.Coins = 0;
        State.FarmingXp = 0;
        State.GardenXp = 0;
        State.Inventory.Clear();
        State.Collections.Clear();
        State.Plots.Clear();
        State.Plots.AddRange(fresh.Plots);
        State.LastPlantedCrop = fresh.LastPlantedCrop;
        State.Stats = new GameStats();
        RaiseChanged();
    }

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
        };

        double seconds = Math.Min(away.TotalSeconds, MaxOfflineTime.TotalSeconds);
        if (seconds > 0)
        {
            report.Counted = TimeSpan.FromSeconds(seconds);
            long coinsBefore = State.Coins;
            long farmingXpBefore = State.FarmingXp;
            long gardenXpBefore = State.GardenXp;

            _quiet = true;
            try
            {
                for (int i = 0; i < State.Plots.Count; i++)
                    SimulatePlotOffline(i, seconds, report);
            }
            finally
            {
                _quiet = false;
            }

            report.Coins = State.Coins - coinsBefore;
            report.FarmingXp = State.FarmingXp - farmingXpBefore;
            report.GardenXp = State.GardenXp - gardenXpBefore;
        }

        report.FarmingLevelAfter = FarmingLevel;
        report.GardenLevelAfter = GardenLevel;
        if (!report.IsEmpty) RaiseChanged();
        return report;
    }

    private void SimulatePlotOffline(int index, double seconds, OfflineReport report)
    {
        var plot = State.Plots[index];
        if (!Crops.TryGet(plot.CropId, out var crop)) return;

        double grownOffline = seconds * OfflineEfficiency;
        if (!State.Settings.FarmerAutoHarvest)
        {
            plot.Growth = Math.Min(crop.GrowSeconds, plot.Growth + grownOffline);
            return;
        }

        // Work in "farmer time": everything runs at OfflineEfficiency speed.
        double firstHarvestAt = crop.GrowSeconds - plot.Growth + OfflineWalkSeconds;
        double cycle = crop.GrowSeconds + OfflineWalkSeconds;
        if (grownOffline < firstHarvestAt)
        {
            plot.Growth = Math.Min(crop.GrowSeconds, plot.Growth + grownOffline);
            return;
        }

        long harvests = 1 + (long)Math.Floor((grownOffline - firstHarvestAt) / cycle);
        double leftover = grownOffline - firstHarvestAt - (harvests - 1) * cycle;

        for (long h = 0; h < harvests; h++)
        {
            var result = HarvestInternal(index, plot, crop, handHarvest: false);
            report.Harvests++;
            report.Drops[crop.Id] = report.Drops.GetValueOrDefault(crop.Id) + result.Drops;
        }
        plot.Growth = Math.Min(crop.GrowSeconds, leftover);
    }

    // ---------------------------------------------------------------- internals

    private HarvestResult HarvestInternal(int plotIndex, PlotState plot, CropDef crop, bool handHarvest)
    {
        int farmingBefore = FarmingLevel;
        int gardenBefore = GardenLevel;
        int tierBefore = GetCollectionTier(crop);

        long drops = RollDrops(crop, handHarvest);

        State.Collections[crop.Id] = GetCollection(crop) + drops;

        long autoSellCoins = 0;
        if (State.Settings.AutoSell)
        {
            autoSellCoins = drops * crop.SellPrice;
            State.Coins += autoSellCoins;
            State.Stats.CoinsEarned += autoSellCoins;
        }
        else
        {
            State.Inventory[crop.Id] = GetInventory(crop) + drops;
        }

        int tierAfter = GetCollectionTier(crop);
        long gardenXp = crop.GardenXp;
        for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
            gardenXp += (long)CollectionTiers.GardenXpPerTier * tier;

        State.FarmingXp += crop.FarmingXp;
        State.GardenXp += gardenXp;
        plot.Growth = 0;

        State.Stats.TotalHarvests++;
        if (handHarvest) State.Stats.HandHarvests++;

        var result = new HarvestResult(plotIndex, crop, drops, crop.FarmingXp, gardenXp, handHarvest, autoSellCoins);
        if (!_quiet) Harvested?.Invoke(result);

        for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
            if (!_quiet) CollectionTierReached?.Invoke(new CollectionTierUp(crop, tier));

        int farmingAfter = FarmingLevel;
        if (farmingAfter > farmingBefore)
            OnFarmingLevelUp(farmingBefore, farmingAfter);

        int gardenAfter = GardenLevel;
        if (gardenAfter > gardenBefore)
            OnGardenLevelUp(gardenBefore, gardenAfter);

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
        var rewards = new List<string>();
        foreach (var crop in Crops.All.Where(c => c.UnlockGardenLevel > oldLevel && c.UnlockGardenLevel <= newLevel))
            rewards.Add($"Unlocked {crop.Name}");

        var next = GardenProgression.NextPlotUnlock(State.Plots.Count);
        if (next is not null && next.GardenLevel > oldLevel && next.GardenLevel <= newLevel)
            rewards.Add($"New plot available ({next.Coins:N0} coins)");

        if (!_quiet)
        {
            LeveledUp?.Invoke(new LevelUp(SkillKind.Garden, oldLevel, newLevel, rewards));
            RaiseChanged();
        }
    }

    private long RollDrops(CropDef crop, bool handHarvest)
    {
        double expected = ExpectedDrops(crop, handHarvest);
        long whole = (long)Math.Floor(expected);
        double extraChance = expected - whole;
        if (_rng.NextDouble() < extraChance) whole++;
        return whole;
    }

    private bool TryGetPlot(int index, out PlotState plot)
    {
        if (index >= 0 && index < State.Plots.Count)
        {
            plot = State.Plots[index];
            return true;
        }
        plot = null!;
        return false;
    }

    private void RaiseChanged()
    {
        if (!_quiet) Changed?.Invoke();
    }
}
