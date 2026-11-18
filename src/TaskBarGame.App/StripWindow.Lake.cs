using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>
/// The lake: while the farmer is fishing, the field row is replaced by water with a wooden pontoon.
/// The farmer casts, the bobber floats, a "!" shows a bite, and the catch flies back to the pontoon.
/// </summary>
public partial class StripWindow
{
    private static readonly Brush FishingBrush = Format.Brush("#6FC3F0");
    private static readonly Brush LineBrush = Format.Brush("#DDE8EEF2");
    private static readonly IReadOnlyList<Brush> SplashBrushes = new[]
    {
        Format.Brush("#CDEEFF"), Format.Brush("#5AA0D8"), Format.Brush("#FFFFFF"),
    };

    /// <summary>The pontoon's deck is this many sprite pixels above the bottom of the farmer's usual spot.</summary>
    private const int PontoonLift = 4;

    /// <summary>The rod picture's handle, where the farmer holds it, and its tip (sprite pixels).</summary>
    private const double RodGripX = 2.5, RodGripY = 12.5, RodTipX = 14.5, RodTipY = 1;

    private readonly List<Image> _water = new();
    private string _builtLocation = Places.Field;
    private Path _line = new();
    private PathFigure _lineFigure = new();
    private QuadraticBezierSegment _lineCurve = new();
    private Image _bobber = new();
    private Image _biteBubble = new();
    private Image _catchImage = new();
    private Border? _lakeArea;
    private Image _travelIcon = new();
    private Grid? _travelButton;
    private double _waterClock;
    private int _waterFrame;
    private AnglerPhase _lastPhase = AnglerPhase.Resting;
    private Point _rodTip;

    // ================================================================== building the lake

    /// <summary>Water across the row, a pontoon for the farmer, and (at Driftwood Cove) Rusty Rowan on his jetty.</summary>
    private void BuildLake(LakeDef lake, int length)
    {
        _water.Clear();

        // The whole lake opens the Fishing tab when clicked.
        var tip = new TextBlock();
        _lakeArea = new Border
        {
            Width = (length + 2) * _tile,
            Height = _tile,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = new ToolTip { Content = tip },
        };
        ToolTipService.SetInitialShowDelay(_lakeArea, 400);
        _lakeArea.MouseLeftButtonUp += (_, e) => { OpenPanelOn("FishingTab"); e.Handled = true; };
        _lakeArea.MouseRightButtonUp += (_, e) => { OpenMainMenu(_lakeArea); e.Handled = true; };
        Place(_stripLayer, _lakeArea, TileLeft(-1), _tileTop);

        for (int slot = -1; slot <= length; slot++)
        {
            var water = SpriteImage(_host.Sprites["water1"], _tile);
            water.IsHitTestVisible = false;
            Place(_stripLayer, water, TileLeft(slot), _tileTop);
            _water.Add(water);
        }
        for (int slot = -1; slot <= 1; slot++) PlaceScenery("pontoon", slot);

        if (lake == Places.DriftwoodCove)
        {
            PlaceScenery("pontoon", length);
            var rowan = SpriteImage(_host.Sprites.Rowan, _tile);
            rowan.Cursor = Cursors.Hand;
            rowan.ToolTip = $"{Trades.ShopkeeperName} trades junk for bait, treasure and more. Click to see his shop.";
            rowan.MouseLeftButtonUp += (_, e) => { OpenPanelOn("FishingTab"); e.Handled = true; };
            rowan.MouseRightButtonUp += (_, e) => { OpenMainMenu(rowan); e.Handled = true; };
            Place(_stripLayer, rowan, TileLeft(length), _tileTop - PontoonLift * _unit);
        }

        // The line, the bobber, the "!" when something bites, and the catch flying in.
        _lineFigure = new PathFigure { IsFilled = false, IsClosed = false };
        _lineCurve = new QuadraticBezierSegment();
        _lineFigure.Segments.Add(_lineCurve);
        _line = new Path
        {
            Stroke = LineBrush,
            StrokeThickness = Math.Max(1, _unit / 2),
            Data = new PathGeometry(new[] { _lineFigure }),
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden,
        };
        _stripLayer.Children.Add(_line);

        _bobber = new Image { Source = _host.Sprites["bobber"], Width = 5 * _unit, Height = 6 * _unit, Stretch = Stretch.Fill };
        _biteBubble = SpriteImage(_host.Sprites["bubble"], _tile);
        _catchImage = SpriteImage(null, 12 * _unit);
        foreach (var image in new[] { _bobber, _biteBubble, _catchImage })
        {
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            image.IsHitTestVisible = false;
            image.Visibility = Visibility.Hidden;
            _stripLayer.Children.Add(image);
        }
        _lastPhase = _host.Angler.Phase;
    }

    private void PlaceScenery(string sprite, int slot)
    {
        var image = SpriteImage(_host.Sprites[sprite], _tile);
        image.IsHitTestVisible = false;
        Place(_stripLayer, image, TileLeft(slot), _tileTop);
    }

    /// <summary>
    /// The button next to the coins that sends the farmer fishing, or back to the field.
    /// Right-click it to pick a place.
    /// </summary>
    private void BuildTravelButton(double x)
    {
        _travelIcon = SpriteImage(null, _tile);
        _travelButton = new Grid
        {
            Width = _tile,
            Height = _tile,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = new ToolTip { Content = new TextBlock() },
        };
        ToolTipService.SetInitialShowDelay(_travelButton, 200);
        _travelButton.Children.Add(_travelIcon);
        _travelButton.MouseLeftButtonUp += (_, e) =>
        {
            TravelTo(Engine.IsFishing ? Places.Field : Engine.LastLake.Id);
            e.Handled = true;
        };
        _travelButton.MouseRightButtonUp += (_, e) => { ShowMenu(TravelMenu(new ContextMenu()), _travelButton); e.Handled = true; };
        Place(_stripLayer, _travelButton, x, _tileTop);
        UpdateTravelButton();
    }

    private void UpdateTravelButton()
    {
        if (_travelButton is null) return;
        var source = Engine.IsFishing ? _host.Sprites.CropIcon(Engine.FieldCrop) : _host.Sprites.RodIcon(Engine.RodTier);
        if (!ReferenceEquals(_travelIcon.Source, source)) _travelIcon.Source = source;
        var tip = (TextBlock)((ToolTip)_travelButton.ToolTip).Content;
        tip.Text = Engine.IsFishing
            ? "Back to the field\nRight-click to go somewhere else"
            : $"Go fishing at {Engine.LastLake.Name}\nRight-click to pick a lake";
    }

    /// <summary>Adds "The field", each lake and the boat trips to a menu.</summary>
    private ContextMenu TravelMenu(ContextMenu menu)
    {
        foreach (var item in TravelItems()) menu.Items.Add(item);
        return menu;
    }

    private IEnumerable<MenuItem> TravelItems()
    {
        var field = MakeItem("The field", () => TravelTo(Places.Field));
        field.Icon = CropMenu.PixelImage(_host.Sprites.CropIcon(Engine.FieldCrop), 16);
        field.FontWeight = !Engine.IsFishing ? FontWeights.Bold : FontWeights.Normal;
        yield return field;

        foreach (var lake in Places.Lakes)
        {
            bool reachable = Engine.CanReach(lake);
            var item = reachable
                ? MakeItem(lake.Name, () => TravelTo(lake.Id))
                : MakeItem($"{lake.Name}  (boat trip: {Format.Number(lake.BoatTripCost)} coins)", () => OpenPanelOn("FishingTab"));
            item.Icon = CropMenu.PixelImage(_host.Sprites.RodIcon(Engine.RodTier), 16);
            item.FontWeight = Engine.Location == lake.Id ? FontWeights.Bold : FontWeights.Normal;
            yield return item;
        }
    }

    private void TravelTo(string place)
    {
        if (Engine.TravelTo(place, out string reason)) _host.Save();
        else QueueToast("CAN'T GO THERE YET", reason, DangerBrush, 4);
    }

    // ================================================================== every frame

    /// <summary>The farmer standing on the pontoon with their rod.</summary>
    private void UpdateAnglerFarmer()
    {
        var angler = _host.Angler;
        double x = Math.Round((TileLeft(0) + AnglerAgent.StandAt * _tile) / _unit) * _unit;
        double top = _tileTop - PontoonLift * _unit;
        bool castingForward = angler.Phase == AnglerPhase.Casting && angler.Progress is > 0.35 and < 0.7;
        string frame = castingForward ? "farmer_swing" : "farmer_farm1";

        var source = _host.Sprites.Farmer(frame, 0, _hatId, _outfitId);
        if (!ReferenceEquals(_farmer.Source, source)) _farmer.Source = source;
        _farmerFlip.ScaleX = 1;
        _farmer.Opacity = 1;
        Canvas.SetLeft(_farmer, x);
        Canvas.SetTop(_farmer, top);
        Canvas.SetLeft(_farmerHit, x + 3 * _unit);
        Canvas.SetTop(_farmerHit, top + 2 * _unit);

        // The rod: held by the handle, swung back and forward to cast, lowered while waiting, yanked up to reel in.
        var (handX, handY) = HandIn(frame);
        var rod = _host.Sprites.RodIcon(Engine.RodTier);
        if (!ReferenceEquals(_tool.Source, rod)) _tool.Source = rod;
        double left = x + (handX + 0.5 - RodGripX) * _unit;
        double rodTop = top + (handY + 0.5 - RodGripY) * _unit;
        Canvas.SetLeft(_tool, left);
        Canvas.SetTop(_tool, rodTop);
        _toolSwing.CenterX = RodGripX * _unit;
        _toolSwing.CenterY = RodGripY * _unit;
        _toolSwing.Angle = RodAngle(angler);
        _tool.Opacity = 1;
        _tool.Visibility = Visibility.Visible;

        double radians = _toolSwing.Angle * Math.PI / 180;
        double dx = (RodTipX - RodGripX) * _unit, dy = (RodTipY - RodGripY) * _unit;
        _rodTip = new Point(
            left + RodGripX * _unit + dx * Math.Cos(radians) - dy * Math.Sin(radians),
            rodTop + RodGripY * _unit + dx * Math.Sin(radians) + dy * Math.Cos(radians));
    }

    /// <summary>Degrees the rod is turned from how it's drawn (clockwise = lowered towards the water).</summary>
    private double RodAngle(AnglerAgent angler)
    {
        double t = angler.Progress;
        return angler.Phase switch
        {
            AnglerPhase.Casting => t < 0.4 ? -50 * t / 0.4 : -50 + 70 * (t - 0.4) / 0.6,
            AnglerPhase.Waiting => 20 + Math.Sin(_time * 1.5) * 2,
            AnglerPhase.Biting => (int)(_time * 10) % 2 == 0 ? 26 : 18,
            AnglerPhase.Reeling => 20 - 55 * Math.Min(1, t / 0.4),
            _ => -35 + 35 * t,
        };
    }

    /// <summary>Ripples the water and moves the bobber, line and catch.</summary>
    private void UpdateLake(double dt)
    {
        _waterClock += dt;
        if (_waterClock >= 0.7)
        {
            _waterClock = 0;
            _waterFrame ^= 1;
            for (int i = 0; i < _water.Count; i++)
                _water[i].Source = _host.Sprites[(i + _waterFrame) % 2 == 0 ? "water1" : "water2"];
        }

        var angler = _host.Angler;
        double surface = _tileTop + 12 * _unit;
        double bobberX = TileLeft(0) + angler.BobberAt * _tile + _tile / 2;
        double floatY = surface - 3 * _unit;

        if (angler.Phase != _lastPhase)
        {
            if (angler.Phase is AnglerPhase.Waiting or AnglerPhase.Biting) Splash(bobberX, surface);
            _lastPhase = angler.Phase;
        }

        Point? bobber = null, lineEnd = null;
        double sag = 0;
        _catchImage.Visibility = Visibility.Hidden;
        switch (angler.Phase)
        {
            case AnglerPhase.Casting when angler.Progress > 0.45:
            {
                double u = (angler.Progress - 0.45) / 0.55;
                bobber = new Point(Lerp(_rodTip.X, bobberX, u), Lerp(_rodTip.Y, floatY, u) - Math.Sin(u * Math.PI) * 6 * _unit);
                sag = 2 * _unit;
                break;
            }
            case AnglerPhase.Waiting:
                bobber = new Point(bobberX, floatY + (Math.Sin(_time * 2.5) > 0.5 ? _unit : 0));
                sag = 4 * _unit;
                break;
            case AnglerPhase.Biting:
                bobber = new Point(bobberX, floatY + ((int)(_time * 8) % 2 == 0 ? 2 * _unit : 0));
                break;
            case AnglerPhase.Reeling when angler.LastCatch is { } caught:
            {
                double u = angler.Progress;
                double handX = Canvas.GetLeft(_farmer) + 12.5 * _unit, handY = Canvas.GetTop(_farmer) + 9 * _unit;
                var at = new Point(Lerp(bobberX, handX, u), Lerp(surface - 6 * _unit, handY, u) - Math.Sin(u * Math.PI) * 10 * _unit);
                _catchImage.Source = _host.Sprites.ItemIcon(caught.Item);
                Canvas.SetLeft(_catchImage, Math.Round(at.X / _unit) * _unit - 6 * _unit);
                Canvas.SetTop(_catchImage, Math.Round(at.Y / _unit) * _unit - 6 * _unit);
                _catchImage.Visibility = Visibility.Visible;
                lineEnd = at;
                break;
            }
        }

        if (bobber is { } b)
        {
            Canvas.SetLeft(_bobber, Math.Round(b.X / _unit) * _unit - 2 * _unit);
            Canvas.SetTop(_bobber, Math.Round(b.Y / _unit) * _unit);
            _bobber.Visibility = Visibility.Visible;
            lineEnd = new Point(b.X + 0.5 * _unit, b.Y + _unit);
        }
        else
        {
            _bobber.Visibility = Visibility.Hidden;
        }

        _biteBubble.Visibility = angler.Phase == AnglerPhase.Biting ? Visibility.Visible : Visibility.Hidden;
        if (angler.Phase == AnglerPhase.Biting)
        {
            Canvas.SetLeft(_biteBubble, Math.Round(bobberX / _unit) * _unit - 6 * _unit);
            Canvas.SetTop(_biteBubble, floatY - 11 * _unit);
        }

        if (lineEnd is { } end)
        {
            _lineFigure.StartPoint = _rodTip;
            _lineCurve.Point1 = new Point((_rodTip.X + end.X) / 2, Math.Max(_rodTip.Y, end.Y) + sag);
            _lineCurve.Point2 = end;
            _line.Visibility = Visibility.Visible;
        }
        else
        {
            _line.Visibility = Visibility.Hidden;
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Drops of water jumping up where the bobber lands, or when something bites.</summary>
    private void Splash(double x, double surface)
    {
        if (_unit <= 0 || !_expanded || _hiddenForFullscreen) return;
        double pixel = Math.Max(2, _unit);
        for (int i = 0; i < 7; i++)
        {
            var shape = new Rectangle
            {
                Width = pixel,
                Height = pixel,
                Fill = SplashBrushes[_rng.Next(SplashBrushes.Count)],
                IsHitTestVisible = false,
            };
            var particle = new Particle(shape)
            {
                X = x + (_rng.NextDouble() * 2 - 1) * 3 * _unit,
                Y = surface - _unit,
                Vx = (_rng.NextDouble() * 2 - 1) * 12 * pixel,
                Vy = -(10 + _rng.NextDouble() * 14) * pixel,
                Gravity = 110 * pixel,
                Life = 0.35 + _rng.NextDouble() * 0.25,
            };
            Place(_fxLayer, shape, particle.X, particle.Y);
            _particles.Add(particle);
        }
    }

    // ================================================================== catches

    private void OnFishCollectionTier(FishCollectionTierUp tierUp)
    {
        QueueToast($"{tierUp.Fish.Name.ToUpperInvariant()} COLLECTION {Format.Roman(tierUp.NewTier)}",
            $"+{FishCollections.TreasurePerTier:0.##}% Treasure Chance · +{FishCollections.FishingXpPerTier * tierUp.NewTier} Fishing XP",
            ViewModels.CropBrushes.Get(Items.Of(tierUp.Fish)), 4, PopupKind.Collection);
    }

    private void OnCaught(CatchResult result)
    {
        var item = result.Item;
        if (_expanded && !_hiddenForFullscreen && _unit > 0 && Settings.ShowFloatingText)
        {
            double x = TileLeft(0) + _host.Angler.BobberAt * _tile + _tile / 2;
            if (result.Treasure) AddTextFloater(result.Great ? "GREAT CATCH!" : "TREASURE!", x, _tileTop - 6 * _unit - _fontSize, GoldBrush, life: 2);
            AddTextFloater($"+{result.Count} {item.Name}", x, _tileTop - _unit, ViewModels.CropBrushes.Get(item));
            AddTextFloater($"+{Format.Number(result.FishingXp, 1)} Fishing XP", x, _tileTop + _fontSize, FishingBrush, delay: 0.15);
            if (result.AutoSoldCoins > 0)
                AddTextFloater($"+{Format.Number(result.AutoSoldCoins)} coins", x, _tileTop + 2 * _fontSize, GoldBrush, delay: 0.3);
        }

        if (result.BaitRanOut && result.Bait is { } bait)
            QueueToast($"OUT OF {bait.Name.ToUpperInvariant()}",
                "The farmer is fishing without bait now. Make more at the bait table or trade junk for it (Fishing tab).",
                DangerBrush, 7, PopupKind.BaitRanOut, ("Get more", () => OpenPanelOn("FishingTab")));

        if (result.Treasure)
        {
            string body = $"{item.Name}! {item.Fishing?.Description}" + (result.First ? " It's new in your catch log." : "");
            QueueToast(result.Great ? "GREAT CATCH!" : "GOOD CATCH!", body.Trim(), GoldBrush, 6, PopupKind.Treasure,
                ("See what it makes", () => OpenPanelOn("FishingTab")));
        }
        else if (result.First)
        {
            QueueToast($"NEW CATCH: {item.Name.ToUpperInvariant()}",
                $"Sells for {Format.Coins((long)Engine.MarketPrice(item))} at the Market. It's in your catch log (Fishing tab).",
                FishingBrush, 5, PopupKind.Collection);
        }
    }
}
