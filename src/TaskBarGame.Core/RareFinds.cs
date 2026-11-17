namespace TaskBarGame.Core;

/// <summary>
/// Something rare that can turn up when a crop is broken. The first one you find goes in your
/// Rare Finds album and gives <see cref="Bonus"/> for good; more of them can be sold.
/// </summary>
/// <param name="OneIn">The chance per crop broken: 1 in this many.</param>
/// <param name="SellPrice">What the Market pays for one (with your Market sell bonus).</param>
public sealed record RareFindDef(string Id, string Name, long OneIn, StatBonus Bonus, long SellPrice)
{
    public string Sprite => "rare_" + Id;
}

/// <summary>How many of a rare find you've found (ever) and how many you're holding.</summary>
public sealed class RareFindState
{
    public long Found { get; set; }
    public long Held { get; set; }
}

/// <summary>A rare find just turned up. <paramref name="First"/> = it's new to your album.</summary>
/// <param name="TileIndex">The crop it came from (-1 while the game was closed).</param>
public sealed record RareDrop(RareFindDef Find, bool First, int TileIndex);

/// <summary>Every rare find, from the most common to the rarest.</summary>
public static class RareFinds
{
    public static readonly IReadOnlyList<RareFindDef> All = new RareFindDef[]
    {
        new("clover", "Four-Leaf Clover", 5_000, new StatBonus(Fortune: 5), 2_000),
        new("seed", "Golden Seed", 20_000, new StatBonus(Speed: 3), 10_000),
        new("coin", "Ancient Coin", 40_000, new StatBonus(Sell: 5), 25_000),
        new("horseshoe", "Lucky Horseshoe", 60_000, new StatBonus(Xp: 5), 40_000),
        new("star", "Star Fragment", 150_000, new StatBonus(Fortune: 10), 100_000),
        new("acorn", "Crystal Acorn", 400_000, new StatBonus(Speed: 5, Xp: 5), 250_000),
    };

    public static RareFindDef? Find(string? id) => All.FirstOrDefault(f => f.Id == id);
}

/// <summary>Rare drops: rolling for them, the album's bonuses, and selling extras.</summary>
public sealed partial class GameEngine
{
    /// <summary>A rare find turned up (not raised for ones found while the game was closed).</summary>
    public event Action<RareDrop>? RareFound;

    public RareFindState RareFind(RareFindDef find) =>
        State.RareFinds.TryGetValue(find.Id, out var state) ? state : new RareFindState();

    /// <summary>How many different rare finds are in your album.</summary>
    public int RareFindsInAlbum => RareFinds.All.Count(f => RareFind(f).Found > 0);

    /// <summary>The album's bonuses: each rare find you've ever found counts once.</summary>
    public StatBonus RareFindsBonus
    {
        get
        {
            var bonus = new StatBonus();
            foreach (var find in RareFinds.All)
                if (RareFind(find).Found > 0) bonus += find.Bonus;
            return bonus;
        }
    }

    /// <summary>What the Market pays for one (with your Market sell bonus).</summary>
    public double RareFindPrice(RareFindDef find) => find.SellPrice * (1 + SellPercent / 100.0);

    /// <summary>Sells up to <paramref name="count"/> of a rare find you're holding. Returns coins earned.</summary>
    public long SellRareFind(string id, long count)
    {
        if (RareFinds.Find(id) is not { } find || count <= 0) return 0;
        var state = RareFind(find);
        long sold = Math.Min(count, state.Held);
        if (sold <= 0) return 0;
        long coins = (long)Math.Floor(sold * RareFindPrice(find));
        state.Held -= sold;
        State.Coins += coins;
        State.Stats.CoinsEarned += coins;
        RaiseChanged();
        return coins;
    }

    /// <summary>One roll per crop broken: at most one rare find, the rarest checked first.</summary>
    private void RollRareFind(int tile)
    {
        double roll = _rng.NextDouble();
        double chance = 0;
        foreach (var find in RareFinds.All.Reverse())
        {
            chance += 1.0 / find.OneIn;
            if (roll >= chance) continue;

            if (!State.RareFinds.TryGetValue(find.Id, out var state))
                State.RareFinds[find.Id] = state = new RareFindState();
            bool first = state.Found == 0;
            state.Found++;
            state.Held++;
            if (!_quiet) RareFound?.Invoke(new RareDrop(find, first, tile));
            return;
        }
    }
}
