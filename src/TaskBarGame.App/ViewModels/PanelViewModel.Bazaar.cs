using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private void InitBazaar()
    {
        var rows = new List<BazaarRow>();
        foreach (var item in Items.All)
        {
            BazaarRow? row = null;
            row = new BazaarRow(item, _host.Sprites.CropIcon(item.Crop), _host.Sprites.ItemContainer(item),
                new RelayCommand(() => Select(row!)));
            rows.Add(row);
        }
        BazaarRows = rows;

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

    /// <summary>Sell offers undercut the current buy price by a little so they fill first.</summary>
    private double OfferPrice(ItemDef item) => Math.Max(0.1, Engine.BazaarBuyPrice(item) - 0.1);

    /// <summary>Buy orders bid a little over the current sell price.</summary>
    private double OrderPrice(ItemDef item) => Engine.BazaarSellPrice(item) + 0.1;

    private void Select(BazaarRow row)
    {
        if (Selected is not null) Selected.IsSelected = false;
        Selected = row;
        row.IsSelected = true;
        BazaarMessage = "";
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
                Orders.Add(new OrderRow(order, _host.Sprites.CropIcon(item.Crop), _host.Sprites.ItemContainer(item),
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
        SellOfferText = $"Sell offer at {Format.Price(OfferPrice(item))} each";
        BuyOrderText = $"Buy order at {Format.Price(OrderPrice(item))} each";

        CanInstantSell = amount > 0 && have >= amount;
        CanSellOffer = CanInstantSell;
        CanInstantBuy = amount > 0 && Engine.State.Coins >= buyTotal;
        CanBuyOrder = amount > 0 && Engine.State.Coins >= (long)Math.Ceiling(amount * OrderPrice(item));
    }
}
