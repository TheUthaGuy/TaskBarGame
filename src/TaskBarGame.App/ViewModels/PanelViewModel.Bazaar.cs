using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>The Bazaar tab: a list of items, a trading box for the selected one, and your orders.</summary>
public sealed partial class PanelViewModel
{
    private BazaarRow? _selected;
    private string _bazaarAmount = "160";
    private string _bazaarMessage = "";
    private string _selectedHaveText = "";
    private string _selectedSellText = "";
    private string _selectedBuyText = "";
    private string _selectedTrendText = "";
    private string _instantSellText = "";
    private string _instantBuyText = "";
    private string _sellOfferText = "";
    private string _buyOrderText = "";
    private bool _canInstantSell;
    private bool _canInstantBuy;
    private bool _canSellOffer;
    private bool _canBuyOrder;
    private bool _hasOrders;
    private string _bazaarSearch = "";
    private string _offerPriceInput = "";
    private string _orderPriceInput = "";
    private string _offerPriceHint = "";
    private string _orderPriceHint = "";
    private string _priceWarning = "";
    private PointCollection _chartPoints = new();
    private PointCollection _chartArea = new();
    private string _chartText = "";
    private bool _hasChart;
    private double _chartWidth = 280;

    /// <summary>Height of the price chart, in the panel's units.</summary>
    public const double ChartHeight = 40;

    public IReadOnlyList<BazaarRow> BazaarRows { get; private set; } = Array.Empty<BazaarRow>();
    public ObservableCollection<OrderRow> Orders { get; } = new();
    public bool HasOrders { get => _hasOrders; private set => Set(ref _hasOrders, value); }

    public BazaarRow? Selected { get => _selected; private set => Set(ref _selected, value); }
    public ImageSource? SelectedIcon => Selected?.Icon;
    public ImageSource? SelectedContainer => Selected?.Container;
    public bool SelectedHasContainer => Selected?.HasContainer == true;
    public string SelectedName => Selected?.Name ?? "";
    public string SelectedHaveText { get => _selectedHaveText; private set => Set(ref _selectedHaveText, value); }
    public string SelectedSellText { get => _selectedSellText; private set => Set(ref _selectedSellText, value); }
    public string SelectedBuyText { get => _selectedBuyText; private set => Set(ref _selectedBuyText, value); }
    public string SelectedTrendText { get => _selectedTrendText; private set => Set(ref _selectedTrendText, value); }

    /// <summary>How many to trade, as typed in the box.</summary>
    public string BazaarAmount
    {
        get => _bazaarAmount;
        set
        {
            if (Set(ref _bazaarAmount, value)) RefreshTradeBox();
        }
    }

    public string BazaarMessage { get => _bazaarMessage; private set => Set(ref _bazaarMessage, value); }
    public string InstantSellText { get => _instantSellText; private set => Set(ref _instantSellText, value); }
    public string InstantBuyText { get => _instantBuyText; private set => Set(ref _instantBuyText, value); }
    public string SellOfferText { get => _sellOfferText; private set => Set(ref _sellOfferText, value); }
    public string BuyOrderText { get => _buyOrderText; private set => Set(ref _buyOrderText, value); }
    public bool CanInstantSell { get => _canInstantSell; private set => Set(ref _canInstantSell, value); }
    public bool CanInstantBuy { get => _canInstantBuy; private set => Set(ref _canInstantBuy, value); }
    public bool CanSellOffer { get => _canSellOffer; private set => Set(ref _canSellOffer, value); }
    public bool CanBuyOrder { get => _canBuyOrder; private set => Set(ref _canBuyOrder, value); }

    public ICommand InstantSellCommand { get; private set; } = null!;
    public ICommand InstantBuyCommand { get; private set; } = null!;
    public ICommand SellOfferCommand { get; private set; } = null!;
    public ICommand BuyOrderCommand { get; private set; } = null!;
    public ICommand AmountOneCommand { get; private set; } = null!;
    public ICommand AmountTenCommand { get; private set; } = null!;
    public ICommand AmountStackCommand { get; private set; } = null!;
    public ICommand AmountAllCommand { get; private set; } = null!;

    /// <summary>The Bazaar search box: hides items whose name doesn't match.</summary>
    public string BazaarSearch
    {
        get => _bazaarSearch;
        set
        {
            if (Set(ref _bazaarSearch, value)) CollectionViewSource.GetDefaultView(BazaarRows).Refresh();
        }
    }

    /// <summary>Your own price for a sell offer, as typed (empty = automatic).</summary>
    public string OfferPriceInput
    {
        get => _offerPriceInput;
        set
        {
            if (Set(ref _offerPriceInput, value)) RefreshTradeBox();
        }
    }

    /// <summary>Your own price for a buy order, as typed (empty = automatic).</summary>
    public string OrderPriceInput
    {
        get => _orderPriceInput;
        set
        {
            if (Set(ref _orderPriceInput, value)) RefreshTradeBox();
        }
    }

    /// <summary>Shown in the empty price boxes, e.g. "Price: auto (2.5)".</summary>
    public string OfferPriceHint { get => _offerPriceHint; private set => Set(ref _offerPriceHint, value); }
    public string OrderPriceHint { get => _orderPriceHint; private set => Set(ref _orderPriceHint, value); }

    /// <summary>A warning when your price is worse than trading straight away.</summary>
    public string PriceWarning { get => _priceWarning; private set => Set(ref _priceWarning, value); }

    /// <summary>The selected item's price over the last 2 hours, as a line (and the area under it).</summary>
    public PointCollection ChartPoints { get => _chartPoints; private set => Set(ref _chartPoints, value); }
    public PointCollection ChartArea { get => _chartArea; private set => Set(ref _chartArea, value); }

    /// <summary>"Last 2 hours: low 2.3 · high 2.6".</summary>
    public string ChartText { get => _chartText; private set => Set(ref _chartText, value); }

    public bool HasChart { get => _hasChart; private set => Set(ref _hasChart, value); }

    /// <summary>How wide the chart is drawn: set by the window when it's laid out.</summary>
    public double ChartWidth
    {
        get => _chartWidth;
        set
        {
            if (value > 0 && Math.Abs(value - _chartWidth) > 0.5)
            {
                _chartWidth = value;
                RefreshChart();
            }
        }
    }

    private void InitBazaar()
    {
        var rows = new List<BazaarRow>();
        foreach (var item in Items.All)
        {
            BazaarRow? row = null;
            row = new BazaarRow(item, _host.Sprites.ItemIcon(item), _host.Sprites.ItemContainer(item),
                new RelayCommand(() => Select(row!)));
            rows.Add(row);
        }
        BazaarRows = rows;
        CollectionViewSource.GetDefaultView(BazaarRows).Filter = o => o is BazaarRow row && row.Matches(BazaarSearch);

        InstantSellCommand = new RelayCommand(() => Trade(item =>
        {
            long coins = Engine.BazaarSell(item.Id, Amount, out string reason);
            return coins > 0 ? $"Sold {Format.Number(Amount)} {item.Name} for {Format.Coins(coins)}." : reason;
        }));
        InstantBuyCommand = new RelayCommand(() => Trade(item =>
        {
            long cost = Engine.BazaarBuy(item.Id, Amount, out string reason);
            return cost > 0 ? $"Bought {Format.Number(Amount)} {item.Name} for {Format.Coins(cost)}." : reason;
        }));
        SellOfferCommand = new RelayCommand(() => Trade(item =>
        {
            var order = Engine.CreateSellOffer(item.Id, Amount, OfferPrice(item), out string reason);
            return order is not null
                ? $"Sell offer placed: {Format.Number(Amount)} {item.Name} at {Format.Price(order.Price)} each. It fills when buyers pay that much."
                : reason;
        }));
        BuyOrderCommand = new RelayCommand(() => Trade(item =>
        {
            var order = Engine.CreateBuyOrder(item.Id, Amount, OrderPrice(item), out string reason);
            return order is not null
                ? $"Buy order placed: {Format.Number(Amount)} {item.Name} at {Format.Price(order.Price)} each. It fills when sellers drop to that price."
                : reason;
        }));
        AmountOneCommand = new RelayCommand(() => BazaarAmount = "1");
        AmountTenCommand = new RelayCommand(() => BazaarAmount = "10");
        AmountStackCommand = new RelayCommand(() => BazaarAmount = "160");
        AmountAllCommand = new RelayCommand(() =>
        {
            if (Selected is not null) BazaarAmount = Math.Max(1, Engine.GetItem(Selected.Item)).ToString();
        });

        Select(rows[0]);
    }

    /// <summary>The amount typed in the box, or 0 if it isn't a number.</summary>
    private long Amount => long.TryParse(BazaarAmount.Replace(",", "").Trim(), out long n) && n > 0 ? n : 0;

    /// <summary>Your sell offer price, or by default a little under the buy price so it fills first.</summary>
    private double OfferPrice(ItemDef item) => TypedPrice(OfferPriceInput) ?? AutoOfferPrice(item);

    /// <summary>Your buy order price, or by default a little over the sell price.</summary>
    private double OrderPrice(ItemDef item) => TypedPrice(OrderPriceInput) ?? AutoOrderPrice(item);

    private double AutoOfferPrice(ItemDef item) => Math.Max(0.1, Engine.BazaarBuyPrice(item) - 0.1);
    private double AutoOrderPrice(ItemDef item) => Engine.BazaarSellPrice(item) + 0.1;

    /// <summary>A price typed in a box, rounded to 0.1, or null if the box is empty or not a price.</summary>
    private static double? TypedPrice(string text)
    {
        text = text.Replace(",", "").Trim();
        if (text.Length == 0) return null;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double price)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out price)) return null;
        return price > 0 && double.IsFinite(price) ? Math.Max(0.1, Math.Round(price, 1)) : null;
    }

    private void Select(BazaarRow row)
    {
        if (Selected is not null) Selected.IsSelected = false;
        Selected = row;
        row.IsSelected = true;
        BazaarMessage = "";
        _offerPriceInput = _orderPriceInput = ""; // each item starts with automatic prices
        Raise(nameof(OfferPriceInput));
        Raise(nameof(OrderPriceInput));
        Raise(nameof(SelectedIcon));
        Raise(nameof(SelectedContainer));
        Raise(nameof(SelectedHasContainer));
        Raise(nameof(SelectedName));
        RefreshTradeBox();
    }

    private void Trade(Func<ItemDef, string> action)
    {
        if (Selected is null) return;
        if (Amount <= 0)
        {
            BazaarMessage = "Type how many you want to trade.";
            return;
        }
        BazaarMessage = action(Selected.Item);
        _host.Save();
        Refresh();
    }

    private void RefreshBazaar()
    {
        foreach (var row in BazaarRows) row.Update(Engine);

        var current = Engine.BazaarOrders.ToList();
        if (!current.SequenceEqual(Orders.Select(o => o.Order)))
        {
            Orders.Clear();
            foreach (var order in current)
            {
                var item = Items.Get(order.ItemId);
                long id = order.Id;
                Orders.Add(new OrderRow(order, _host.Sprites.ItemIcon(item), _host.Sprites.ItemContainer(item),
                    new RelayCommand(() =>
                    {
                        Engine.CancelOrder(id);
                        BazaarMessage = "Order cancelled. Anything that hadn't filled was given back.";
                        Refresh();
                    })));
            }
        }
        foreach (var row in Orders) row.Update();
        HasOrders = Orders.Count > 0;

        RefreshTradeBox();
    }

    private void RefreshTradeBox()
    {
        if (Selected is null) return;
        var item = Selected.Item;
        long amount = Amount;
        long have = Engine.GetItem(item);
        double sell = Engine.BazaarSellPrice(item);
        double buy = Engine.BazaarBuyPrice(item);
        long sellTotal = (long)Math.Floor(amount * sell);
        long buyTotal = (long)Math.Ceiling(amount * buy);

        SelectedHaveText = $"You have {Format.Number(have)} · the Market pays {Format.Price(Math.Round(Engine.MarketPrice(item), 1))}";
        SelectedSellText = $"Sell price: {Format.Price(sell)} coins each";
        SelectedBuyText = $"Buy price: {Format.Price(buy)} coins each";
        double trend = Engine.BazaarTrend(item);
        SelectedTrendText = Math.Abs(trend) < 0.005
            ? "The price has been steady for the last hour."
            : $"{Format.Trend(trend)} in the last hour.";

        InstantSellText = $"Sell now  +{Format.Number(sellTotal)}";
        InstantBuyText = $"Buy now  −{Format.Number(buyTotal)}";
        double offer = OfferPrice(item);
        double order = OrderPrice(item);
        SellOfferText = $"Sell offer at {Format.Price(offer)} each";
        BuyOrderText = $"Buy order at {Format.Price(order)} each";
        OfferPriceHint = $"Your price (auto: {Format.Price(AutoOfferPrice(item))})";
        OrderPriceHint = $"Your price (auto: {Format.Price(AutoOrderPrice(item))})";

        bool badOffer = OfferPriceInput.Trim().Length > 0 && TypedPrice(OfferPriceInput) is null;
        bool badOrder = OrderPriceInput.Trim().Length > 0 && TypedPrice(OrderPriceInput) is null;
        PriceWarning = badOffer || badOrder ? "Type a price like 2.5, or leave the box empty for the automatic price."
            : offer <= sell ? $"Your sell offer is at or below the sell price: \"Sell now\" pays {Format.Price(sell)} straight away."
            : order >= buy ? $"Your buy order is at or above the buy price: \"Buy now\" costs {Format.Price(buy)} straight away."
            : "";

        bool locked = Engine.IsLocked(item);
        CanInstantSell = amount > 0 && have >= amount && !locked;
        CanSellOffer = CanInstantSell && !badOffer;
        CanInstantBuy = amount > 0 && Engine.State.Coins >= buyTotal;
        CanBuyOrder = amount > 0 && !badOrder && Engine.State.Coins >= (long)Math.Ceiling(amount * order);
        if (locked) SelectedHaveText += " · locked";

        RefreshChart();
    }

    /// <summary>Draws the selected item's sell price over the last 2 hours (one point every 5 minutes, plus now).</summary>
    private void RefreshChart()
    {
        if (Selected is null) return;
        var item = Selected.Item;
        var prices = Engine.BazaarHistory(item).Append(Engine.BazaarMidPrice(item))
            .Select(mid => mid * (1 - GameEngine.BazaarSpread))
            .ToList();
        HasChart = prices.Count >= 3;
        if (!HasChart)
        {
            ChartText = "The price chart fills in over the next few minutes.";
            ChartPoints = ChartArea = new PointCollection();
            return;
        }

        double low = prices.Min(), high = prices.Max();
        double mid = (low + high) / 2;
        double range = Math.Max(high - low, mid * 0.02); // a steady price draws as a nearly flat line
        double bottom = mid - range / 2;
        double width = ChartWidth, height = ChartHeight, pad = 3;

        var line = new PointCollection(prices.Count);
        for (int i = 0; i < prices.Count; i++)
        {
            double x = width * i / (prices.Count - 1);
            double y = pad + (height - 2 * pad) * (1 - (prices[i] - bottom) / range);
            line.Add(new Point(x, y));
        }
        var area = new PointCollection(line) { new Point(width, height), new Point(0, height) };
        line.Freeze();
        area.Freeze();
        ChartPoints = line;
        ChartArea = area;

        int minutes = (prices.Count - 1) * 5;
        string span = minutes >= 60 ? $"{minutes / 60.0:0.#} hours" : $"{minutes} minutes";
        ChartText = $"Sell price, last {span}: low {Format.Price(Math.Round(low, 1))} · high {Format.Price(Math.Round(high, 1))}";
    }
}
