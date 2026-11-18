namespace TaskBarGame.Core;

/// <summary>What came up on one catch.</summary>
/// <param name="Count">How many (2 on a double catch).</param>
/// <param name="Treasure">A treasure catch (a crafting material).</param>
/// <param name="Great">A rare "Great catch!" treasure.</param>
/// <param name="First">The first one ever caught: new in the catch log.</param>
/// <param name="Bait">The bait used up on this catch, or null.</param>
/// <param name="BaitRanOut">That was the last of the chosen bait.</param>
/// <param name="AutoSoldCoins">Coins from selling it straight away (auto-sell is on for it).</param>
public sealed record CatchResult(
    ItemDef Item,
    long Count,
    double FishingXp,
    bool Treasure,
    bool Great,
    bool First,
    ItemDef? Bait,
    bool BaitRanOut = false,
    long AutoSoldCoins = 0);

/// <summary>A fish collection reached a new tier.</summary>
public sealed record FishCollectionTierUp(FishingItemDef Fish, int NewTier);

/// <summary>One line of "What's biting": an item, its chance per catch and the level it needs.</summary>
public sealed record BiteChance(CatchEntry Entry, double Percent, bool Unlocked);

/// <summary>Fishing: where the farmer is, catching, treasure, bait, the Fishing skill, trades and boat trips.</summary>
public sealed partial class GameEngine
{
    /// <summary>Average seconds for a fish to bite with no Fishing Speed.</summary>
    public const double BaseWaitSeconds = 20;

    /// <summary>Seconds of every catch spent casting, on the bite and reeling in (see <see cref="AnglerAgent"/>).</summary>
    public const double CatchOverheadSeconds = AnglerAgent.CastDuration + AnglerAgent.BiteDuration
        + AnglerAgent.ReelDuration + AnglerAgent.RestDuration;

    /// <summary>Treasure Chance everyone has, in percent.</summary>
    public const double BaseTreasureChance = 3;

    /// <summary>A catch was reeled in (not raised for ones caught while the game was closed).</summary>
    public event Action<CatchResult>? Caught;

    /// <summary>The farmer went somewhere else (the field or a lake).</summary>
    public event Action? Travelled;

    /// <summary>A fish collection reached a new tier (not raised while the game was closed).</summary>
    public event Action<FishCollectionTierUp>? FishCollectionTierReached;

    public int FishingLevel => FishingSkill.LevelForXp(State.Fishing.Xp);

    // ================================================================= where the farmer is

    /// <summary>"field", or the id of the lake the farmer is fishing on.</summary>
    public string Location => State.Fishing.Location;

    /// <summary>The lake the farmer is on, or null while they're at the field.</summary>
    public LakeDef? CurrentLake => Places.FindLake(State.Fishing.Location);

    public bool IsFishing => CurrentLake is not null;

    /// <summary>Where "Go fishing" goes: the lake you fished on last.</summary>
    public LakeDef LastLake => Places.FindLake(State.Fishing.LastLake) is { } lake && CanReach(lake) ? lake : Places.WillowLake;

    /// <summary>Lakes on your island are always open; others need their boat trip first.</summary>
    public bool CanReach(LakeDef lake) => !lake.IsOnAnotherIsland || State.Fishing.BoatTrips.Contains(lake.Id);

    /// <summary>Sends the farmer to the field (<see cref="Places.Field"/>) or a lake you can reach.</summary>
    public bool TravelTo(string place, out string reason)
    {
        reason = "";
        if (place == State.Fishing.Location) return true;
        if (place != Places.Field)
        {
            if (Places.FindLake(place) is not { } lake)
            {
                reason = "There's no such place";
                return false;
            }
            if (!CanReach(lake))
            {
                reason = $"Buy the boat trip to {lake.Island} first";
                return false;
            }
        }
        State.Fishing.Location = place;
        if (place != Places.Field) State.Fishing.LastLake = place;
        if (!_quiet) Travelled?.Invoke();
        RaiseChanged();
        return true;
    }

    public bool CanBuyBoatTrip(LakeDef lake, out string reason)
    {
        if (!lake.IsOnAnotherIsland || State.Fishing.BoatTrips.Contains(lake.Id))
        {
            reason = "Already open";
            return false;
        }
        if (State.Coins < lake.BoatTripCost)
        {
            reason = $"Needs {lake.BoatTripCost - State.Coins:N0} more coins";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>Pays for the boat trip to a lake on another island. You can travel there for free after that.</summary>
    public bool BuyBoatTrip(LakeDef lake, out string reason)
    {
        if (!CanBuyBoatTrip(lake, out reason)) return false;
        State.Coins -= lake.BoatTripCost;
        State.Fishing.BoatTrips.Add(lake.Id);
        RaiseChanged();
        return true;
    }

    // ================================================================= equipment & stats

    public int RodTier => State.Fishing.Rod;

    public int GetFishingArmourTier(ArmourSlot slot) => State.Fishing.Armour.GetValueOrDefault(slot);

    /// <summary>The best fishing armour set you're wearing all four pieces of (0 = none).</summary>
    public int FullFishingSetTier => Armour.Slots.Min(GetFishingArmourTier);

    /// <summary>The bait that's chosen, or null.</summary>
    public ItemDef? ChosenBait => State.Fishing.Bait is { } id && Baits.IsBait(id) ? Items.Get(id) : null;

    /// <summary>The bait the next catch will use: the chosen one, if you have any left.</summary>
    public ItemDef? ActiveBait => ChosenBait is { } bait && GetItem(bait) > 0 ? bait : null;

    /// <summary>Chooses a bait (null = none). It's used up one per catch while you have some.</summary>
    public void SetBait(string? itemId)
    {
        State.Fishing.Bait = Baits.IsBait(itemId) ? itemId : null;
        RaiseChanged();
    }

    /// <summary>Where your fishing stats come from, for the Fishing tab.</summary>
    public IReadOnlyList<(string Source, FishingBonus Bonus)> FishingSources()
    {
        var sources = new List<(string, FishingBonus)>
        {
            ("Everyone", new FishingBonus(Treasure: BaseTreasureChance)),
            ($"Fishing Level {FishingLevel}", new FishingBonus(Speed: FishingLevel * FishingSkill.SpeedPerLevel)),
            (Rods.Name(RodTier), Rods.Bonus(RodTier)),
        };
        foreach (var slot in Armour.Slots)
            sources.Add((FishingArmour.Name(slot, GetFishingArmourTier(slot)), FishingArmour.Bonus(GetFishingArmourTier(slot))));
        sources.Add(($"{FishingArmour.SetName(FullFishingSetTier)} set bonus", FishingArmour.SetBonus(FullFishingSetTier)));
        int tiers = FishCollectionTiers;
        sources.Add(($"Fish collections ({tiers} tier{(tiers == 1 ? "" : "s")})", new FishingBonus(Treasure: tiers * FishCollections.TreasurePerTier)));
        if (ActiveBait is { } bait) sources.Add((bait.Name, Baits.Bonus[bait.Id]));
        return sources.Where(s => !s.Item2.IsEmpty).ToList();
    }

    public FishingBonus FishingStats
    {
        get
        {
            var total = new FishingBonus();
            foreach (var (_, bonus) in FishingSources()) total += bonus;
            return total;
        }
    }

    public int FishingSpeed => FishingStats.Speed;

    /// <summary>Chance in percent that a catch is treasure.</summary>
    public double TreasureChance => Math.Min(100, FishingStats.Treasure);

    /// <summary>Chance in percent that a (non-treasure) catch comes up two at a time.</summary>
    public double DoubleCatchChance => Math.Min(100, FishingStats.DoubleCatch);

    /// <summary>Average seconds for a fish to bite: <see cref="BaseWaitSeconds"/> ÷ (1 + Fishing Speed ÷ 100).</summary>
    public double AverageWaitSeconds => BaseWaitSeconds / (1 + Math.Max(0, FishingSpeed) / 100.0);

    /// <summary>Average seconds from one catch to the next.</summary>
    public double CatchCycleSeconds => AverageWaitSeconds + CatchOverheadSeconds;

    public double CatchesPerHour => 3600 / CatchCycleSeconds;

    // ================================================================= what's biting

    /// <summary>Everything that can bite on a lake, with its chance per catch at your Fishing level.</summary>
    public IReadOnlyList<BiteChance> BiteChances(LakeDef lake)
    {
        var unlocked = lake.Catches.Where(c => c.FishingLevel <= FishingLevel).ToList();
        double total = unlocked.Sum(c => c.Weight);
        double notTreasure = 1 - TreasureChance / 100;
        return lake.Catches
            .Select(c => new BiteChance(c,
                c.FishingLevel <= FishingLevel && total > 0 ? c.Weight / total * notTreasure * 100 : 0,
                c.FishingLevel <= FishingLevel))
            .ToList();
    }

    /// <summary>Chance in percent of each treasure per catch.</summary>
    public IReadOnlyList<(TreasureEntry Entry, double Percent)> TreasureChances()
    {
        double total = Places.Treasure.Sum(t => t.Weight);
        return Places.Treasure.Select(t => (t, t.Weight / total * TreasureChance)).ToList();
    }

    /// <summary>
    /// Roughly how many coins an hour fishing on a lake makes if you sold every catch to the Market
    /// (with your Fishing Speed, Treasure Chance and Double Catch).
    /// </summary>
    public double FishingCoinsPerHour(LakeDef lake)
    {
        double perCatch = BiteChances(lake).Sum(b => b.Percent / 100 * MarketPrice(b.Entry.Item))
            * (1 + DoubleCatchChance / 100)
            + TreasureChances().Sum(t => t.Percent / 100 * MarketPrice(t.Entry.Item));
        return perCatch * CatchesPerHour;
    }

    /// <summary>How many of an item you've ever caught.</summary>
    public long TimesCaught(ItemDef item) => State.Fishing.Caught.GetValueOrDefault(item.Id);

    /// <summary>A fish's collection tier: how many of it you've ever caught, against its thresholds.</summary>
    public int FishCollectionTier(FishingItemDef fish) => FishCollections.TierFor(fish, TimesCaught(Items.Of(fish)));

    /// <summary>Every fish collection's tiers added up (each gives <see cref="FishCollections.TreasurePerTier"/>% Treasure Chance).</summary>
    public int FishCollectionTiers => FishCollections.Fish.Sum(FishCollectionTier);

    // ================================================================= auto-sell

    /// <summary>Sell this fishing item to the Market as soon as it's caught (bait never is).</summary>
    public bool IsAutoSelling(ItemDef item) =>
        item.Crop is { } crop ? item.Kind == ItemKind.Crop && IsAutoSelling(crop)
            : item.Kind != ItemKind.Bait && State.Settings.AutoSellItems.Contains(item.Id);

    public void SetAutoSell(ItemDef item, bool autoSell)
    {
        if (item.Crop is { } crop)
        {
            SetAutoSell(crop, autoSell);
            return;
        }
        if (autoSell && item.Kind != ItemKind.Bait) State.Settings.AutoSellItems.Add(item.Id);
        else State.Settings.AutoSellItems.Remove(item.Id);
        RaiseChanged();
    }

    // ================================================================= Rusty Rowan's trades you can make

    /// <summary>
    /// Junk shop trades you could make right now, apart from bait (which is nearly always ready):
    /// the treasure and clothes.
    /// </summary>
    public IEnumerable<TradeDef> ReadyJunkTrades() =>
        Trades.From(TradeSource.JunkShop).Where(t => t.Result?.Kind != ItemKind.Bait && MaxTrades(t) > 0);

    // ================================================================= catching

    /// <summary>
    /// Reels in one catch on the lake the farmer is on (nothing at the field). Uses up one bait if
    /// one is chosen, rolls for treasure, then for what bit, and maybe a double catch.
    /// </summary>
    public CatchResult? Catch() => CurrentLake is { } lake ? CatchInternal(lake) : null;

    private CatchResult CatchInternal(LakeDef lake)
    {
        int levelBefore = FishingLevel;

        // The bait counts for this catch, then it's used up.
        double treasureChance = TreasureChance, doubleChance = DoubleCatchChance;
        var bait = ActiveBait;
        if (bait is not null) AddItem(bait, -1);

        ItemDef item;
        long count = 1;
        double xp;
        bool treasure = false, great = false;
        if (_rng.NextDouble() * 100 < treasureChance)
        {
            var entry = Pick(Places.Treasure, t => t.Weight);
            item = entry.Item;
            xp = entry.Xp;
            treasure = true;
            great = entry.Great;
        }
        else
        {
            var entry = Pick(lake.Catches.Where(c => c.FishingLevel <= levelBefore).ToList(), c => c.Weight);
            item = entry.Item;
            if (_rng.NextDouble() * 100 < doubleChance) count = 2;
            xp = entry.Xp * count;
        }

        bool first = TimesCaught(item) == 0;
        var fish = FishCollections.Has(item.Id) ? item.Fishing : null;
        int tierBefore = fish is null ? 0 : FishCollectionTier(fish);
        AddItem(item, count);
        State.Fishing.Caught[item.Id] = TimesCaught(item) + count;
        State.Fishing.Xp += xp;
        State.Fishing.Catches++;
        if (treasure) State.Fishing.Treasures++;

        // Auto-sell (what isn't locked or kept for upgrades).
        long soldCoins = 0;
        if (IsAutoSelling(item))
        {
            long sell = Math.Min(count, SellableAmount(item));
            if (sell > 0) soldCoins = SellToMarket(item, sell);
        }

        // Fish collection tiers: Fishing XP now, and Treasure Chance for good.
        int tierAfter = fish is null ? 0 : FishCollectionTier(fish);
        for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
            State.Fishing.Xp += (double)tier * FishCollections.FishingXpPerTier;

        bool ranOut = bait is not null && GetItem(bait) == 0;
        var result = new CatchResult(item, count, xp, treasure, great, first, bait, ranOut, soldCoins);
        if (!_quiet)
        {
            Caught?.Invoke(result);
            for (int tier = tierBefore + 1; tier <= tierAfter; tier++)
                FishCollectionTierReached?.Invoke(new FishCollectionTierUp(fish!, tier));
            RaiseChanged();
        }
        if (FishingLevel > levelBefore) OnFishingLevelUp(levelBefore, FishingLevel);
        return result;
    }

    private T Pick<T>(IReadOnlyList<T> entries, Func<T, int> weight)
    {
        double roll = _rng.NextDouble() * entries.Sum(weight);
        foreach (var entry in entries)
        {
            roll -= weight(entry);
            if (roll < 0) return entry;
        }
        return entries[^1];
    }

    private void OnFishingLevelUp(int oldLevel, int newLevel)
    {
        long coins = 0;
        for (int level = oldLevel + 1; level <= newLevel; level++)
            coins += (long)level * FishingSkill.CoinRewardPerLevel;
        State.Coins += coins;

        bool NewAt(int level) => level > oldLevel && level <= newLevel;
        var rewards = new List<string>
        {
            $"+{(newLevel - oldLevel) * FishingSkill.SpeedPerLevel} Fishing Speed",
            $"+{coins:N0} coins",
        };
        foreach (var lake in Places.Lakes)
        foreach (var entry in lake.Catches.Where(c => NewAt(c.FishingLevel)))
            rewards.Add($"New catch at {lake.Name}: {entry.Item.Name}");
        int recipes = Recipes.All.Count(r => r.IsFishing && NewAt(r.FishingLevel))
            + Trades.All.Count(t => NewAt(t.FishingLevel));
        if (recipes > 0) rewards.Add($"{recipes} new thing{(recipes == 1 ? "" : "s")} to craft");

        if (!_quiet)
        {
            LeveledUp?.Invoke(new LevelUp(SkillKind.Fishing, oldLevel, newLevel, rewards));
            RaiseChanged();
        }
    }

    /// <summary>Fishing while the game was closed: catches at <see cref="OfflineEfficiency"/> speed.</summary>
    private void FishOffline(LakeDef lake, double seconds, OfflineReport report)
    {
        double xpBefore = State.Fishing.Xp;
        long treasuresBefore = State.Fishing.Treasures;
        long coinsBefore = State.Coins;
        long catches = (long)Math.Floor(seconds / CatchCycleSeconds * OfflineEfficiency);
        for (long i = 0; i < catches; i++)
        {
            var result = CatchInternal(lake);
            report.FishCaught += result.Count;
            report.Caught[result.Item.Id] = report.Caught.GetValueOrDefault(result.Item.Id) + result.Count;
            if (result.BaitRanOut) report.BaitRanOut = result.Bait!.Name;
        }
        report.Catches = catches;
        report.FishingXp = State.Fishing.Xp - xpBefore;
        report.Treasures = State.Fishing.Treasures - treasuresBefore;
        report.Coins = State.Coins - coinsBefore;
    }

    // ================================================================= trades: bait and Rusty Rowan

    /// <summary>Rusty Rowan's shop opens once you've bought the boat trip to Driftwood Cove.</summary>
    public bool IsJunkShopOpen => State.Fishing.BoatTrips.Contains(Places.DriftwoodCove.Id);

    public bool OwnsFishingCosmetic(string id) => State.Fishing.OwnedCosmetics.Contains(id);

    public bool CanTrade(TradeDef trade, out string reason)
    {
        if (trade.Source == TradeSource.JunkShop && !IsJunkShopOpen)
        {
            reason = $"Buy the boat trip to {Places.DriftwoodCove.Island} first";
            return false;
        }
        if (FishingLevel < trade.FishingLevel)
        {
            reason = $"Needs Fishing Level {trade.FishingLevel}";
            return false;
        }
        if (trade.Cosmetic is { } cosmetic && OwnsFishingCosmetic(cosmetic))
        {
            reason = "Already yours";
            return false;
        }
        foreach (var ingredient in trade.Cost)
        {
            long have = GetItem(ingredient.ItemId);
            if (have < ingredient.Count)
            {
                reason = $"Needs {ingredient.Count - have:N0} more {ingredient.Item.Name}";
                return false;
            }
        }
        if (State.Coins < trade.Coins)
        {
            reason = $"Needs {trade.Coins - State.Coins:N0} more coins";
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>How many times in a row you could make a trade right now.</summary>
    public long MaxTrades(TradeDef trade)
    {
        if (!CanTrade(trade, out _)) return 0;
        if (trade.IsOneOff) return 1;
        long times = long.MaxValue;
        foreach (var ingredient in trade.Cost)
            times = Math.Min(times, GetItem(ingredient.ItemId) / ingredient.Count);
        if (trade.Coins > 0) times = Math.Min(times, State.Coins / trade.Coins);
        return times == long.MaxValue ? 1 : times;
    }

    /// <summary>Makes a trade up to <paramref name="times"/> times. Returns how many times it was made.</summary>
    public long Trade(TradeDef trade, long times, out string reason)
    {
        reason = "";
        if (times <= 0) return 0;
        if (!CanTrade(trade, out reason)) return 0;
        long made = Math.Min(times, MaxTrades(trade));
        if (made <= 0) return 0;

        foreach (var ingredient in trade.Cost)
            AddItem(ingredient.Item, -ingredient.Count * made);
        State.Coins -= trade.Coins * made;
        if (trade.Result is { } result) AddItem(result, (long)trade.ResultCount * made);
        if (trade.Cosmetic is { } cosmetic) State.Fishing.OwnedCosmetics.Add(cosmetic);

        RaiseChanged();
        return made;
    }
}
