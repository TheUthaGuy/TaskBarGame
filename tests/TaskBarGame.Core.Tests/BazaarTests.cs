using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class BazaarTests
{
    private static readonly ItemDef Wheat = Items.Raw(Crops.Wheat);
    private static readonly ItemDef PackedWheat = Items.Packed(Crops.Wheat);

    /// <summary>A roll of 0.5 means "no random wobble", so prices only move for the reasons we test.</summary>
    private static GameEngine NewEngine(long coins = 0)
    {
        var engine = new GameEngine(GameState.CreateNew(), new FixedRandom(0.5));
        engine.State.Coins = coins;
        return engine;
    }

    private static void Give(GameEngine engine, ItemDef item, long count) =>
        engine.State.Inventory[item.Id] = engine.GetItem(item) + count;

    private static void Minutes(GameEngine engine, int minutes) => engine.Tick(60.0 * minutes);

    [Fact]
    public void Every_item_starts_at_its_normal_price_with_a_spread()
    {
        var engine = NewEngine();
        foreach (var item in Items.All)
        {
            double mid = GameEngine.BazaarBasePrice(item);
            Assert.Equal(mid, engine.BazaarMidPrice(item), precision: 6);
            Assert.True(engine.BazaarBuyPrice(item) > engine.BazaarSellPrice(item), item.Name);
            Assert.True(engine.BazaarSellPrice(item) >= item.SellPrice, $"{item.Name}: the Bazaar should pay at least the Market");
        }
    }

    [Fact]
    public void Instant_sell_pays_coins_and_lowers_the_price()
    {
        var engine = NewEngine();
        Give(engine, Wheat, 1_000);
        double priceBefore = engine.BazaarMidPrice(Wheat);
        double sellPrice = engine.BazaarSellPrice(Wheat);

        long coins = engine.BazaarSell(Wheat.Id, 1_000, out _);

        Assert.Equal((long)Math.Floor(1_000 * sellPrice), coins);
        Assert.Equal(coins, engine.State.Coins);
        Assert.Equal(0, engine.GetItem(Wheat));
        Assert.True(engine.BazaarMidPrice(Wheat) < priceBefore);
    }

    [Fact]
    public void Instant_buy_costs_coins_and_raises_the_price()
    {
        var engine = NewEngine(coins: 10_000);
        double priceBefore = engine.BazaarMidPrice(PackedWheat);
        double buyPrice = engine.BazaarBuyPrice(PackedWheat);

        long cost = engine.BazaarBuy(PackedWheat.Id, 3, out _);

        Assert.Equal((long)Math.Ceiling(3 * buyPrice), cost);
        Assert.Equal(10_000 - cost, engine.State.Coins);
        Assert.Equal(3, engine.GetItem(PackedWheat));
        Assert.True(engine.BazaarMidPrice(PackedWheat) > priceBefore);
    }

    [Fact]
    public void You_cant_trade_what_you_dont_have()
    {
        var engine = NewEngine(coins: 10);
        Give(engine, Wheat, 5);

        Assert.Equal(0, engine.BazaarSell(Wheat.Id, 6, out var reason));
        Assert.Contains("only have 5", reason);
        Assert.Equal(0, engine.BazaarBuy(PackedWheat.Id, 1, out reason));
        Assert.Contains("more coins", reason);
        Assert.Equal(10, engine.State.Coins);
        Assert.Equal(5, engine.GetItem(Wheat));
    }

    [Fact]
    public void Prices_drift_back_towards_normal()
    {
        var engine = NewEngine();
        Give(engine, Wheat, 100_000);
        engine.BazaarSell(Wheat.Id, 100_000, out _);
        double crashed = engine.BazaarMidPrice(Wheat);
        Assert.True(crashed < GameEngine.BazaarBasePrice(Wheat));

        Minutes(engine, 120);

        Assert.True(engine.BazaarMidPrice(Wheat) > crashed);
        Assert.Equal(GameEngine.BazaarBasePrice(Wheat), engine.BazaarMidPrice(Wheat), precision: 1);
    }

    [Fact]
    public void Prices_stay_in_a_range_so_buying_here_never_beats_selling_to_the_market()
    {
        var engine = NewEngine();
        engine.State.Talismans[TalismanFamily.Merchant] = 3; // the best Market price there is
        foreach (var item in Items.All)
        {
            Give(engine, item, 1_000_000);
            engine.BazaarSell(item.Id, 1_000_000, out _); // crash every price as low as it goes
            Assert.True(engine.BazaarBuyPrice(item) >= engine.MarketPrice(item) - 1e-9,
                $"{item.Name}: buy {engine.BazaarBuyPrice(item)} vs Market {engine.MarketPrice(item)}");
        }
    }

    [Fact]
    public void A_buy_order_sets_coins_aside_and_fills_over_time()
    {
        var engine = NewEngine(coins: 10_000);
        double price = engine.BazaarSellPrice(Wheat) + 0.1;
        var fills = new List<BazaarFill>();
        engine.OrderFilled += fills.Add;

        var order = engine.CreateBuyOrder(Wheat.Id, 1_000, price, out _)!;

        Assert.Equal(10_000 - (long)Math.Ceiling(1_000 * price), engine.State.Coins);
        Assert.Equal(0, engine.GetItem(Wheat));

        Minutes(engine, 5);

        Assert.Equal(1_000, engine.GetItem(Wheat));
        Assert.True(order.IsDone);
        Assert.Empty(engine.BazaarOrders);
        Assert.Equal(1_000, fills.Sum(f => f.Amount));
    }

    [Fact]
    public void A_sell_offer_sets_items_aside_and_pays_as_it_fills()
    {
        var engine = NewEngine();
        Give(engine, Wheat, 500);
        double price = engine.BazaarBuyPrice(Wheat) - 0.1;

        var order = engine.CreateSellOffer(Wheat.Id, 500, price, out _)!;
        Assert.Equal(0, engine.GetItem(Wheat));

        Minutes(engine, 1);
        Assert.Equal(100, order.Filled); // a fifth a minute
        Assert.Equal((long)Math.Floor(100 * order.Price), engine.State.Coins);

        Minutes(engine, 4);
        Assert.True(order.IsDone);
        Assert.Equal((long)Math.Floor(500 * order.Price), engine.State.Coins);
    }

    [Fact]
    public void Orders_wait_while_the_price_is_wrong()
    {
        var engine = NewEngine(coins: 10_000);
        var cheap = engine.CreateBuyOrder(Wheat.Id, 100, 0.5, out _)!; // far below the market

        Minutes(engine, 30);

        Assert.Equal(0, cheap.Filled);
        Assert.Single(engine.BazaarOrders);
    }

    [Fact]
    public void Cancelling_gives_back_what_hasnt_filled()
    {
        var engine = NewEngine(coins: 1_000);
        Give(engine, Wheat, 50);
        var buy = engine.CreateBuyOrder(Wheat.Id, 100, 1.0, out _)!;
        var sell = engine.CreateSellOffer(Wheat.Id, 50, 99, out _)!;
        Assert.Equal(900, engine.State.Coins);

        Assert.True(engine.CancelOrder(buy.Id));
        Assert.True(engine.CancelOrder(sell.Id));
        Assert.False(engine.CancelOrder(sell.Id));

        Assert.Equal(1_000, engine.State.Coins);
        Assert.Equal(50, engine.GetItem(Wheat));
        Assert.Empty(engine.BazaarOrders);
    }

    [Fact]
    public void Orders_need_the_coins_or_items_up_front()
    {
        var engine = NewEngine(coins: 10);
        Assert.Null(engine.CreateBuyOrder(Wheat.Id, 100, 2, out var reason));
        Assert.Contains("more coins", reason);
        Assert.Null(engine.CreateSellOffer(Wheat.Id, 1, 2, out reason));
        Assert.Contains("only have 0", reason);
    }

    [Fact]
    public void Prices_wobble_and_keep_a_history()
    {
        var engine = new GameEngine(GameState.CreateNew(), new Random(7));
        Minutes(engine, 90);

        var history = engine.BazaarHistory(Wheat);
        Assert.InRange(history.Count, 17, 24);
        Assert.True(history.Distinct().Count() > 1);
        Assert.InRange(engine.BazaarTrend(Wheat), -0.5, 0.5);
        Assert.All(Items.All, i => Assert.InRange(engine.BazaarMidPrice(i),
            i.SellPrice * GameEngine.BazaarMinPrice - 1e-9, i.SellPrice * GameEngine.BazaarMaxPrice + 1e-9));
    }

    [Fact]
    public void Orders_fill_while_you_are_away()
    {
        var engine = NewEngine();
        Give(engine, PackedWheat, 10);
        var offer = engine.CreateSellOffer(PackedWheat.Id, 10, engine.BazaarBuyPrice(PackedWheat) - 0.1, out _)!;

        engine.ApplyOfflineProgress(TimeSpan.FromMinutes(30));

        Assert.True(offer.IsDone);
        Assert.True(engine.State.Coins > 0);
    }

    [Fact]
    public void Bazaar_survives_a_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), "tbg-bazaar-" + Guid.NewGuid().ToString("N"), "save.json");
        try
        {
            var engine = NewEngine(coins: 5_000);
            engine.CreateBuyOrder(Wheat.Id, 100, 1.0, out _);
            Minutes(engine, 10);
            var saves = new SaveManager(path);
            saves.Save(engine.State);

            var loaded = new GameEngine(saves.LoadOrCreate());

            Assert.Single(loaded.BazaarOrders);
            Assert.Equal(engine.BazaarMidPrice(Wheat), loaded.BazaarMidPrice(Wheat), precision: 6);
            Assert.Equal(engine.BazaarHistory(Wheat), loaded.BazaarHistory(Wheat));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
