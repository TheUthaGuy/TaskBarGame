namespace TaskBarGame.Core;

public enum BazaarOrderKind
{
    /// <summary>You want to buy: coins are set aside now, items arrive as it fills.</summary>
    BuyOrder,

    /// <summary>You want to sell: items are set aside now, coins arrive as it fills.</summary>
    SellOffer,
}

/// <summary>A buy order or sell offer waiting on the Bazaar.</summary>
public sealed class BazaarOrder
{
    public long Id { get; set; }
    public string ItemId { get; set; } = "";
    public BazaarOrderKind Kind { get; set; }
    public long Amount { get; set; }
    public long Filled { get; set; }

    /// <summary>Coins per item.</summary>
    public double Price { get; set; }

    public long Remaining => Amount - Filled;
    public bool IsDone => Filled >= Amount;
}

/// <summary>The Bazaar's prices and your orders, saved with the game.</summary>
public sealed class BazaarState
{
    /// <summary>The middle price of each item, by item id. Buying costs a bit more, selling pays a bit less.</summary>
    public Dictionary<string, double> Prices { get; set; } = new();

    /// <summary>Recent prices of each item, one every few minutes, oldest first.</summary>
    public Dictionary<string, List<double>> History { get; set; } = new();

    public List<BazaarOrder> Orders { get; set; } = new();
    public long NextOrderId { get; set; } = 1;

    /// <summary>Seconds towards the next Bazaar minute (prices move once a minute).</summary>
    public double MinuteClock { get; set; }

    public int MinutesSinceSample { get; set; }

    public void Normalize()
    {
        Prices ??= new();
        History ??= new();
        Orders ??= new();
        foreach (var id in Prices.Keys.ToList())
        {
            if (!Items.TryGet(id, out var item)) Prices.Remove(id);
            else Prices[id] = GameEngine.ClampBazaarPrice(item, Prices[id]);
        }
        foreach (var id in History.Keys.ToList())
            if (!Items.TryGet(id, out _) || History[id] is null) History.Remove(id);
        Orders.RemoveAll(o => o is null || !Items.TryGet(o.ItemId, out _) || o.Amount <= 0 || o.IsDone);
        NextOrderId = Math.Max(NextOrderId, Orders.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1);
        if (!double.IsFinite(MinuteClock) || MinuteClock < 0) MinuteClock = 0;
    }
}

/// <param name="Delivered">Items (buy order) or coins (sell offer) that just arrived.</param>
public sealed record BazaarFill(BazaarOrder Order, long Amount, long Delivered);

/// <summary>
/// The Bazaar: buy and sell crops, packed crops and dense crops with other (pretend) farmers.
///
/// Every item has a price that drifts a little each minute and drifts back towards normal over
/// time. Selling lots pushes the price down; buying lots pushes it up. Buy orders and sell offers
/// fill bit by bit while the price is on their side.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>On average the Bazaar pays this much more than the Market.</summary>
    public const double BazaarPremium = 1.25;

    /// <summary>Buying costs this much above the middle price, selling pays this much below.</summary>
    public const double BazaarSpread = 0.04;

    /// <summary>
    /// The middle price never leaves this range (times the Market price). The bottom is high enough
    /// that buying here and selling to the Market (even with a Merchant's Relic) never makes money.
    /// </summary>
    public const double BazaarMinPrice = 1.16, BazaarMaxPrice = 1.8;

    /// <summary>How many crops' worth you can trade before the price moves by 1%.</summary>
    public const double BazaarCropsPerPercent = 4_000;

    /// <summary>A buy order or sell offer fills at most this much of its amount per minute.</summary>
    public const double OrderFillPerMinute = 0.2;

    private const int MinutesPerSample = 5;
    private const int SamplesKept = 24;

    public event Action<BazaarFill>? OrderFilled;

    public static double BazaarBasePrice(ItemDef item) => item.SellPrice * BazaarPremium;

    internal static double ClampBazaarPrice(ItemDef item, double price) =>
        double.IsFinite(price)
            ? Math.Clamp(price, item.SellPrice * BazaarMinPrice, item.SellPrice * BazaarMaxPrice)
            : BazaarBasePrice(item);

    public double BazaarMidPrice(ItemDef item) =>
        State.Bazaar.Prices.TryGetValue(item.Id, out var price) ? price : BazaarBasePrice(item);

    /// <summary>Coins you get for selling one item right now.</summary>
    public double BazaarSellPrice(ItemDef item) => RoundPrice(BazaarMidPrice(item) * (1 - BazaarSpread));

    /// <summary>Coins one item costs to buy right now.</summary>
    public double BazaarBuyPrice(ItemDef item) => RoundPrice(BazaarMidPrice(item) * (1 + BazaarSpread));

    /// <summary>How much the price has moved over about the last hour, e.g. 0.05 = up 5%.</summary>
    public double BazaarTrend(ItemDef item)
    {
        if (!State.Bazaar.History.TryGetValue(item.Id, out var history) || history.Count == 0) return 0;
        double then = history[Math.Max(0, history.Count - 60 / MinutesPerSample)];
        return then > 0 ? BazaarMidPrice(item) / then - 1 : 0;
    }

    public IReadOnlyList<double> BazaarHistory(ItemDef item) =>
        State.Bazaar.History.TryGetValue(item.Id, out var history) ? history : Array.Empty<double>();

    public IReadOnlyList<BazaarOrder> BazaarOrders => State.Bazaar.Orders;

    /// <summary>Sells items straight away at the Bazaar's sell price. Returns coins earned.</summary>
    public long BazaarSell(string itemId, long amount, out string reason)
    {
        reason = "";
        if (!Items.TryGet(itemId, out var item) || amount <= 0) return 0;
        if (GetItem(item) < amount)
        {
            reason = $"You only have {GetItem(item):N0} {item.Name}";
            return 0;
        }

        long coins = (long)Math.Floor(amount * BazaarSellPrice(item));
        AddItem(item, -amount);
        State.Coins += coins;
        State.Stats.CoinsEarned += coins;
        MovePrice(item, -amount);
        RaiseChanged();
        return coins;
    }

    /// <summary>Buys items straight away at the Bazaar's buy price. Returns coins spent.</summary>
    public long BazaarBuy(string itemId, long amount, out string reason)
    {
        reason = "";
        if (!Items.TryGet(itemId, out var item) || amount <= 0) return 0;
        long cost = (long)Math.Ceiling(amount * BazaarBuyPrice(item));
        if (State.Coins < cost)
        {
            reason = $"Needs {cost - State.Coins:N0} more coins";
            return 0;
        }

        State.Coins -= cost;
        AddItem(item, amount);
        MovePrice(item, amount);
        RaiseChanged();
        return cost;
    }

    /// <summary>
    /// Places a buy order: the coins are set aside now, and the items arrive while the Bazaar's
    /// sell price is at or below <paramref name="price"/>.
    /// </summary>
    public BazaarOrder? CreateBuyOrder(string itemId, long amount, double price, out string reason)
    {
        reason = "";
        if (!Items.TryGet(itemId, out var item) || amount <= 0 || !(price > 0)) return null;
        price = RoundPrice(price);
        long cost = (long)Math.Ceiling(amount * price);
        if (State.Coins < cost)
        {
            reason = $"Needs {cost - State.Coins:N0} more coins";
            return null;
        }

        State.Coins -= cost;
        return AddOrder(item, BazaarOrderKind.BuyOrder, amount, price);
    }

    /// <summary>
    /// Places a sell offer: the items are set aside now, and the coins arrive while the Bazaar's
    /// buy price is at or above <paramref name="price"/>.
    /// </summary>
    public BazaarOrder? CreateSellOffer(string itemId, long amount, double price, out string reason)
    {
        reason = "";
        if (!Items.TryGet(itemId, out var item) || amount <= 0 || !(price > 0)) return null;
        if (GetItem(item) < amount)
        {
            reason = $"You only have {GetItem(item):N0} {item.Name}";
            return null;
        }

        AddItem(item, -amount);
        return AddOrder(item, BazaarOrderKind.SellOffer, amount, RoundPrice(price));
    }

    /// <summary>Cancels an order and gives back whatever hasn't filled yet.</summary>
    public bool CancelOrder(long orderId)
    {
        var order = State.Bazaar.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return false;

        var item = Items.Get(order.ItemId);
        if (order.Kind == BazaarOrderKind.BuyOrder)
            State.Coins += (long)Math.Floor(order.Remaining * order.Price);
        else
            AddItem(item, order.Remaining);

        State.Bazaar.Orders.Remove(order);
        RaiseChanged();
        return true;
    }

    // ---------------------------------------------------------------- internals

    private BazaarOrder AddOrder(ItemDef item, BazaarOrderKind kind, long amount, double price)
    {
        var order = new BazaarOrder
        {
            Id = State.Bazaar.NextOrderId++,
            ItemId = item.Id,
            Kind = kind,
            Amount = amount,
            Price = price,
        };
        State.Bazaar.Orders.Add(order);
        RaiseChanged();
        return order;
    }

    private void EnsureBazaarPrices()
    {
        foreach (var item in Items.All)
            if (!State.Bazaar.Prices.ContainsKey(item.Id))
                State.Bazaar.Prices[item.Id] = BazaarBasePrice(item);
    }

    /// <summary>Trading pushes the price: positive = bought (price up), negative = sold (price down).</summary>
    private void MovePrice(ItemDef item, long amount)
    {
        double percent = Math.Clamp(amount * item.CropsInside / BazaarCropsPerPercent, -15, 15);
        State.Bazaar.Prices[item.Id] = ClampBazaarPrice(item, BazaarMidPrice(item) * (1 + percent / 100));
    }

    private void BazaarTick(double seconds)
    {
        var bazaar = State.Bazaar;
        bazaar.MinuteClock += seconds;
        while (bazaar.MinuteClock >= 60)
        {
            bazaar.MinuteClock -= 60;
            BazaarMinute();
        }
    }

    private void BazaarMinute()
    {
        var bazaar = State.Bazaar;
        bool sample = ++bazaar.MinutesSinceSample >= MinutesPerSample;
        if (sample) bazaar.MinutesSinceSample = 0;

        foreach (var item in Items.All)
        {
            // Wander a little, and drift back towards the normal price.
            double price = BazaarMidPrice(item);
            price += (BazaarBasePrice(item) - price) * 0.03;
            price *= 1 + (_rng.NextDouble() * 2 - 1) * 0.015;
            bazaar.Prices[item.Id] = ClampBazaarPrice(item, price);

            if (sample)
            {
                if (!bazaar.History.TryGetValue(item.Id, out var history))
                    bazaar.History[item.Id] = history = new List<double>();
                history.Add(bazaar.Prices[item.Id]);
                if (history.Count > SamplesKept) history.RemoveAt(0);
            }
        }

        foreach (var order in bazaar.Orders.ToList())
            TryFillOrder(order);
        bazaar.Orders.RemoveAll(o => o.IsDone);
    }

    private void TryFillOrder(BazaarOrder order)
    {
        var item = Items.Get(order.ItemId);
        bool priceIsRight = order.Kind == BazaarOrderKind.BuyOrder
            ? BazaarSellPrice(item) <= order.Price
            : BazaarBuyPrice(item) >= order.Price;
        if (!priceIsRight) return;

        long amount = Math.Min(order.Remaining, Math.Max(1, (long)Math.Ceiling(order.Amount * OrderFillPerMinute)));
        order.Filled += amount;

        long delivered;
        if (order.Kind == BazaarOrderKind.BuyOrder)
        {
            AddItem(item, amount);
            delivered = amount;
        }
        else
        {
            delivered = (long)Math.Floor(amount * order.Price);
            State.Coins += delivered;
            State.Stats.CoinsEarned += delivered;
        }

        if (!_quiet) OrderFilled?.Invoke(new BazaarFill(order, amount, delivered));
        RaiseChanged();
    }

    /// <summary>Prices are shown to one decimal place; tiny prices never round down to nothing.</summary>
    private static double RoundPrice(double price) => Math.Max(0.1, Math.Round(price, 1));
}
