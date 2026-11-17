using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TaskBarGame.App.Interop;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>
/// The garden strip above the taskbar: plots, the walking farmer, a small coins/levels display,
/// floating "+3 Wheat" numbers and level-up popups.
/// </summary>
public partial class StripWindow : Window
{
    /// <summary>See-through space above the strip where popups float up.</summary>
    private const double PopupSpace = 140;

    /// <summary>The window is at least this wide so popups have room even with few plots.</summary>
    private const double MinWindowWidth = 360;

    /// <summary>Gap between the strip and the left/right edge of the screen.</summary>
    private const double ScreenMargin = 8;

    private static readonly Brush StripBrush = Format.Brush("#E61B231C");
    private static readonly Brush GrassBrush = Format.Brush("#5C8A45");
    private static readonly Brush HoverBrush = Format.Brush("#30FFFFFF");
    private static readonly Brush TrackBrush = Format.Brush("#50000000");
    private static readonly Brush TextBrush = Format.Brush("#EEF3EA");
    private static readonly Brush GoldBrush = Format.Brush("#F5C542");
    private static readonly Brush XpBrush = Format.Brush("#5FD4E8");
    private static readonly Brush GardenBrush = Format.Brush("#7BC950");
    private static readonly Brush DangerBrush = Format.Brush("#E5675A");
    private static readonly Brush ToastBrush = Format.Brush("#F21B231C");
    private static readonly Effect TextShadow = CreateTextShadow();

    private readonly GameHost _host;
    private readonly List<PlotView> _plots = new();
    private readonly List<Floater> _floaters = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();
    private readonly Queue<Toast> _toastQueue = new();
    private readonly DispatcherTimer _collapseTimer;
    private readonly ScaleTransform _farmerFlip = new(1, 1);
    private readonly TranslateTransform _lockBob = new();

    // Layout, in WPF units (1/96 inch). Recalculated by BuildLayout().
    private double _unit;        // size of one sprite pixel
    private double _tile;        // size of one 16x16 sprite
    private double _gap;         // space between plots
    private double _pad;         // padding inside the strip
    private double _fontSize;
    private double _stripLeft;   // where the strip starts inside the window
    private double _stripWidth;
    private double _stripHeight;
    private double _tileTop;     // y of the plots and the farmer
    private double _homeX;       // where the farmer stands while the strip is tucked away

    private Canvas _stripLayer = new();
    private Canvas _homeLayer = new();
    private Canvas _fxLayer = new();
    private Image _farmer = new();
    private Rectangle _farmerHit = new();
    private Grid? _lockSlot;
    private TextBlock? _lockTip;
    private TextBlock _coinsText = new();
    private TextBlock _levelsText = new();
    private Border _ripeBadge = new();
    private TextBlock _ripeBadgeText = new();

    private IntPtr _hwnd;
    private int _builtPlotCount = -1;
    private bool _expanded = true;
    private bool _menuOpen;
    private bool _hiddenForFullscreen;
    private bool _lockReady;
    private bool _layoutRequested;
    private double _layoutDpi;
    private double _time;
    private double _slowTimer;
    private double _verySlowTimer;
    private Toast? _toast;
    private PanelWindow? _panel;


    /// <summary>The garden panel, if it's open.</summary>
    public PanelWindow? Panel => _panel;

    public StripWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (ShouldStayExpanded()) return;
            SetExpanded(false);
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeOverlayWindow(_hwnd);
        };
        Loaded += (_, _) =>
        {
            _expanded = Settings.StripMode == StripMode.Always;
            RequestLayout();
        };
        DpiChanged += (_, e) =>
        {
            // Only rebuild when the scaling really changed: this event can fire repeatedly without a change.
            if (Math.Abs(e.NewDpi.DpiScaleX - _layoutDpi) > 0.001) RequestLayout();
        };
        MouseEnter += (_, _) => OnMouseEntered();
        MouseLeave += (_, _) => OnMouseLeft();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        _host.Frame += OnFrame;
        _host.CaughtUp += report =>
        {
            if (!report.IsEmpty) ShowOfflineReport(report);
        };
        _host.SettingsChanged += () =>
        {
            RequestLayout();
            if (Settings.StripMode == StripMode.Always) SetExpanded(true);
            else if (!ShouldStayExpanded()) SetExpanded(false);
        };
        Engine.Harvested += OnHarvested;
        Engine.LeveledUp += OnLeveledUp;
        Engine.CollectionTierReached += OnCollectionTier;
        Engine.Changed += () =>
        {
            if (Engine.Plots.Count != _builtPlotCount) RequestLayout();
        };
    }

    private GameEngine Engine => _host.Engine;
    private GameSettings Settings => _host.Settings;
    private bool HoverMode => Settings.StripMode == StripMode.OnHover;

    // ================================================================== layout

    /// <summary>
    /// Rebuilds the strip as soon as WPF is free. Never rebuild straight from an event: events like
    /// Loaded can fire in the middle of WPF's own layout pass, and changing the strip's children then
    /// crashes ("the collection changed").
    /// </summary>
    private void RequestLayout()
    {
        if (_layoutRequested) return;
        _layoutRequested = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _layoutRequested = false;
            BuildLayout();
        });
    }

    /// <summary>Creates every element of the strip. Called when plots, size, position or DPI change.</summary>
    private void BuildLayout()
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _layoutDpi = dpi;
        double baseScale = Settings.Size switch
        {
            StripSize.Small => 2,
            StripSize.Large => 4,
            _ => 3,
        };
        // Round to whole screen pixels so the pixel art stays crisp at any Windows scaling.
        _unit = Math.Max(1, Math.Round(baseScale * dpi)) / dpi;
        _tile = 16 * _unit;
        _gap = _unit;
        _pad = 3 * _unit;
        _fontSize = baseScale switch { 2 => 10, 3 => 12, _ => 14 };

        int plotCount = Engine.Plots.Count;
        bool hasLockSlot = GardenProgression.NextPlotUnlock(plotCount) is not null;
        int slots = plotCount + (hasLockSlot ? 1 : 0);
        double plotsWidth = slots * _tile + (slots - 1) * _gap;
        double hudWidth = _fontSize * 10.5;

        _stripWidth = _pad + plotsWidth + 2 * _pad + hudWidth + _pad;
        _stripHeight = 21 * _unit; // 2 padding + 16 tile + 1 progress bar + 2 padding
        _tileTop = PopupSpace + 2 * _unit;

        double windowWidth = Math.Max(_stripWidth, MinWindowWidth);
        Width = windowWidth;
        Height = PopupSpace + _stripHeight;
        Root.Width = Width;
        Root.Height = Height;

        _stripLeft = Settings.Anchor switch
        {
            StripAnchor.Right => windowWidth - _stripWidth,
            StripAnchor.Center => Math.Round((windowWidth - _stripWidth) / 2),
            _ => 0,
        };
        double homePadWidth = _tile + 2 * _pad;
        double homePadLeft = Settings.Anchor switch
        {
            StripAnchor.Right => _stripLeft + _stripWidth - homePadWidth,
            StripAnchor.Center => _stripLeft + Math.Round((_stripWidth - homePadWidth) / 2),
            _ => _stripLeft,
        };
        _homeX = homePadLeft + _pad;

        Root.Children.Clear();
        _plots.Clear();
        _floaters.Clear();
        _particles.Clear();

        _stripLayer = new Canvas();
        _homeLayer = new Canvas();
        _fxLayer = new Canvas { IsHitTestVisible = true };
        Root.Children.Add(_stripLayer);
        Root.Children.Add(_homeLayer);

        BuildStrip(plotCount, hasLockSlot, plotsWidth, hudWidth);
        BuildHomePad(homePadLeft, homePadWidth);
        BuildFarmer();
        Root.Children.Add(_fxLayer);

        _builtPlotCount = plotCount;
        ApplyExpanded();
        Reposition();
        UpdatePlots();
        UpdateSlow();
        if (_toast is not null) ShowToastElement(_toast);
    }

    private void BuildStrip(int plotCount, bool hasLockSlot, double plotsWidth, double hudWidth)
    {
        var panel = new Border
        {
            Width = _stripWidth,
            Height = _stripHeight,
            Background = StripBrush,
            BorderBrush = GrassBrush,
            BorderThickness = new Thickness(0, _unit, 0, 0),
            CornerRadius = new CornerRadius(3 * _unit, 3 * _unit, 0, 0),
        };
        panel.MouseRightButtonUp += (_, e) => { OpenMainMenu(panel); e.Handled = true; };
        Place(_stripLayer, panel, _stripLeft, PopupSpace);

        for (int i = 0; i < plotCount; i++)
            _plots.Add(BuildPlot(i));

        if (hasLockSlot)
            BuildLockSlot(PlotLeft(plotCount));
        else
        {
            _lockSlot = null;
            _lockTip = null;
        }

        // Coins and levels, to the right of the plots. Clicking it opens the panel.
        _coinsText = new TextBlock
        {
            Foreground = GoldBrush,
            FontWeight = FontWeights.Bold,
            FontSize = _fontSize + 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_unit * 1.5, 0, 0, 0),
        };
        _levelsText = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = _fontSize - 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, _unit * 0.5, 0, 0),
        };
        var coinRow = new StackPanel { Orientation = Orientation.Horizontal };
        coinRow.Children.Add(CropMenu.PixelImage(_host.Sprites["coin"], Math.Round(_fontSize * 1.1)));
        coinRow.Children.Add(_coinsText);

        var hud = new StackPanel
        {
            Width = hudWidth,
            Height = _tile,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Open your garden (right-click for options)",
        };
        hud.Children.Add(new Border { Height = Math.Max(0, (_tile - _fontSize * 2.9) / 2) });
        hud.Children.Add(coinRow);
        hud.Children.Add(_levelsText);
        hud.MouseLeftButtonUp += (_, e) => { TogglePanel(); e.Handled = true; };
        hud.MouseRightButtonUp += (_, e) => { OpenMainMenu(hud); e.Handled = true; };
        Place(_stripLayer, hud, _stripLeft + _pad + plotsWidth + 2 * _pad, _tileTop);
    }

    private PlotView BuildPlot(int index)
    {
        double x = PlotLeft(index);
        var hover = new Border { Background = HoverBrush, CornerRadius = new CornerRadius(_unit), Visibility = Visibility.Hidden };
        var crop = SpriteImage(null, _tile);
        var sparkle = SpriteImage(_host.Sprites["sparkle"], 5 * _unit);
        sparkle.HorizontalAlignment = HorizontalAlignment.Right;
        sparkle.VerticalAlignment = VerticalAlignment.Top;
        sparkle.Margin = new Thickness(0, 0, _unit, 0);
        sparkle.Visibility = Visibility.Hidden;

        var tip = new TextBlock();
        var cell = new Grid
        {
            Width = _tile,
            Height = _tile,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = new ToolTip { Content = tip },
        };
        ToolTipService.SetInitialShowDelay(cell, 400);
        cell.Children.Add(hover);
        cell.Children.Add(SpriteImage(_host.Sprites["soil"], _tile));
        cell.Children.Add(crop);
        cell.Children.Add(sparkle);
        cell.MouseEnter += (_, _) => hover.Visibility = Visibility.Visible;
        cell.MouseLeave += (_, _) => hover.Visibility = Visibility.Hidden;
        cell.MouseLeftButtonUp += (_, e) => { OnPlotClicked(index, cell); e.Handled = true; };
        cell.MouseRightButtonUp += (_, e) => { OpenPlotMenu(index, cell); e.Handled = true; };
        Place(_stripLayer, cell, x, _tileTop);

        var track = new Rectangle { Width = _tile, Height = _unit, Fill = TrackBrush };
        var progress = new Rectangle { Width = 0, Height = _unit };
        Place(_stripLayer, track, x, _tileTop + _tile);
        Place(_stripLayer, progress, x, _tileTop + _tile);

        return new PlotView(index, x, crop, sparkle, progress, tip);
    }

    private void BuildLockSlot(double x)
    {
        _lockTip = new TextBlock();
        var lockIcon = SpriteImage(_host.Sprites["lock"], _tile);
        lockIcon.RenderTransform = _lockBob;
        _lockSlot = new Grid
        {
            Width = _tile,
            Height = _tile,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Opacity = 0.85,
            ToolTip = new ToolTip { Content = _lockTip },
        };
        ToolTipService.SetInitialShowDelay(_lockSlot, 200);
        _lockSlot.Children.Add(SpriteImage(_host.Sprites["soil_locked"], _tile));
        _lockSlot.Children.Add(lockIcon);
        _lockSlot.MouseLeftButtonUp += (_, e) => { OnLockClicked(x); e.Handled = true; };
        _lockSlot.MouseRightButtonUp += (_, e) => { OpenMainMenu(_lockSlot); e.Handled = true; };
        Place(_stripLayer, _lockSlot, x, _tileTop);
    }

    /// <summary>The small patch the farmer stands on while the strip is hidden (hover mode).</summary>
    private void BuildHomePad(double left, double width)
    {
        var pad = new Border
        {
            Width = width,
            Height = _stripHeight,
            Background = StripBrush,
            BorderBrush = GrassBrush,
            BorderThickness = new Thickness(0, _unit, 0, 0),
            CornerRadius = new CornerRadius(3 * _unit, 3 * _unit, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Hover to see your garden, click to open it",
        };
        pad.MouseLeftButtonUp += (_, e) => { TogglePanel(); e.Handled = true; };
        pad.MouseRightButtonUp += (_, e) => { OpenMainMenu(pad); e.Handled = true; };
        Place(_homeLayer, pad, left, PopupSpace);
        var soil = SpriteImage(_host.Sprites["soil"], _tile);
        soil.IsHitTestVisible = false;
        Place(_homeLayer, soil, _homeX, _tileTop);

        // Gold bubble showing how many crops are ready.
        _ripeBadgeText = new TextBlock
        {
            Foreground = Brushes.Black,
            FontWeight = FontWeights.Bold,
            FontSize = _fontSize - 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ripeBadge = new Border
        {
            Background = GoldBrush,
            CornerRadius = new CornerRadius(_fontSize),
            MinWidth = _fontSize * 1.4,
            Height = _fontSize * 1.4,
            Padding = new Thickness(3, 0, 3, 0),
            Child = _ripeBadgeText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Place(_homeLayer, _ripeBadge, left + width - _fontSize * 1.2, PopupSpace - _fontSize * 0.6);
    }

    private void BuildFarmer()
    {
        _farmer = SpriteImage(_host.Sprites["farmer_idle"], _tile);
        _farmer.IsHitTestVisible = false;
        _farmer.RenderTransformOrigin = new Point(0.5, 0.5);
        _farmer.RenderTransform = _farmerFlip;
        Root.Children.Add(_farmer);
        Canvas.SetTop(_farmer, _tileTop);

        // Only the farmer's head is clickable, so you can still click the crop they're standing on.
        _farmerHit = new Rectangle
        {
            Width = 10 * _unit,
            Height = 8 * _unit,
            Fill = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Click me to open your garden!",
        };
        _farmerHit.MouseLeftButtonUp += (_, e) => { TogglePanel(); e.Handled = true; };
        _farmerHit.MouseRightButtonUp += (_, e) => { OpenMainMenu(_farmerHit); e.Handled = true; };
        Root.Children.Add(_farmerHit);
        Canvas.SetTop(_farmerHit, _tileTop + 2 * _unit);
    }

    private double PlotLeft(int index) => _stripLeft + _pad + index * (_tile + _gap);

    private Image SpriteImage(ImageSource? source, double size)
    {
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    /// <summary>Sits the window just above the taskbar.</summary>
    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        double left = Settings.Anchor switch
        {
            StripAnchor.Right => area.Right - ScreenMargin - Width,
            StripAnchor.Center => area.Left + (area.Width - Width) / 2,
            _ => area.Left + ScreenMargin,
        };
        left = Math.Max(area.Left, Math.Min(left, area.Right - Width));
        double top = area.Bottom - Height;
        if (Math.Abs(Left - left) > 0.5) Left = left;
        if (Math.Abs(Top - top) > 0.5) Top = top;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
            Dispatcher.BeginInvoke(Reposition);
    }

    // ================================================================== show / hide on hover

    private void OnMouseEntered()
    {
        _collapseTimer.Stop();
        if (HoverMode && !_expanded) SetExpanded(true);
    }

    private void OnMouseLeft()
    {
        if (HoverMode && !ShouldStayExpanded()) _collapseTimer.Start();
    }

    private bool ShouldStayExpanded() =>
        !HoverMode || IsMouseOver || _menuOpen || _panel is not null;

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        ApplyExpanded();
    }

    private void ApplyExpanded()
    {
        _stripLayer.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        _homeLayer.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        UpdateFarmer();
    }

    // ================================================================== every frame

    private void OnFrame(double dt)
    {
        if (_unit <= 0) return; // not laid out yet
        _time += dt;
        UpdatePlots();
        _lockBob.Y = _lockReady ? -Math.Round(Math.Abs(Math.Sin(_time * 3)) * 2) * _unit : 0;
        UpdateFarmer();
        UpdateFloaters(dt);
        UpdateParticles(dt);
        UpdateToast(dt);

        _slowTimer += dt;
        if (_slowTimer >= 0.25)
        {
            _slowTimer = 0;
            UpdateSlow();
        }

        _verySlowTimer += dt;
        if (_verySlowTimer >= 1.5)
        {
            _verySlowTimer = 0;
            UpdateWindowState();
        }
    }

    private void UpdatePlots()
    {
        if (!_expanded) return;
        foreach (var view in _plots)
        {
            if (view.Index >= Engine.Plots.Count) continue;
            var plot = Engine.Plots[view.Index];

            var source = _host.Sprites.ForPlot(plot);
            if (!ReferenceEquals(view.Crop.Source, source)) view.Crop.Source = source;

            bool ripe = GameEngine.IsRipe(plot);
            bool sparkleOn = ripe && ((_time * 1.5 + view.Index * 0.37) % 1.0) < 0.5;
            view.Sparkle.Visibility = sparkleOn ? Visibility.Visible : Visibility.Hidden;

            double progress = GameEngine.GrowthProgress(plot);
            view.Progress.Width = Math.Round(_tile * progress / _unit) * _unit;
            var brush = ripe ? GoldBrush
                : Crops.TryGet(plot.CropId, out var crop) ? CropBrush(crop)
                : TrackBrush;
            if (!ReferenceEquals(view.Progress.Fill, brush)) view.Progress.Fill = brush;
        }
    }

    private void UpdateFarmer()
    {
        var farmer = _host.Farmer;
        bool blink = _time % 4.0 < 0.15;
        string frame;
        double x;
        int facing;

        if (!_expanded)
        {
            frame = blink ? "farmer_blink" : "farmer_idle";
            x = _homeX;
            facing = 1;
        }
        else
        {
            bool step = (int)(farmer.ActionTime / 0.14) % 2 == 0;
            frame = farmer.IsSwinging ? "farmer_swing"
                : farmer.Action != FarmerAction.Walking ? (blink ? "farmer_blink" : "farmer_idle")
                : farmer.IsFarming ? (step ? "farmer_farm1" : "farmer_farm2")
                : (step ? "farmer_walk1" : "farmer_walk2");
            x = Math.Round((PlotLeft(0) + farmer.Position * (_tile + _gap)) / _unit) * _unit;
            facing = farmer.Facing;
        }

        var source = _host.Sprites[frame];
        if (!ReferenceEquals(_farmer.Source, source)) _farmer.Source = source;
        _farmerFlip.ScaleX = facing;
        Canvas.SetLeft(_farmer, x);
        Canvas.SetLeft(_farmerHit, x + 3 * _unit);
    }

    /// <summary>Things that don't need updating 30 times a second.</summary>
    private void UpdateSlow()
    {
        _coinsText.Text = Format.Number(Engine.State.Coins);
        _levelsText.Text = $"Farming {Engine.FarmingLevel} · Garden {Engine.GardenLevel}";

        foreach (var view in _plots)
        {
            if (view.Index < Engine.Plots.Count)
                view.Tip.Text = DescribePlot(view.Index);
        }

        if (_lockSlot is not null && _lockTip is not null)
        {
            bool canUnlock = Engine.CanUnlockNextPlot(out var cost, out string reason);
            _lockTip.Text = cost is null
                ? "All plots unlocked"
                : $"Plot {Engine.Plots.Count + 1}: {Format.Number(cost.Coins)} coins, Garden Level {cost.GardenLevel}\n"
                  + (canUnlock ? "Click to unlock!" : reason);
            _lockSlot.Opacity = canUnlock ? 1 : 0.7;
            _lockReady = canUnlock;
        }

        int ripe = Engine.Plots.Count(GameEngine.IsRipe);
        _ripeBadgeText.Text = ripe.ToString();
        _ripeBadge.Visibility = !_expanded && ripe > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Keeps the strip on top, in place, and out of the way of fullscreen apps.</summary>
    private void UpdateWindowState()
    {
        if (_hwnd == IntPtr.Zero) return;

        bool hide = Settings.HideInFullscreen && NativeMethods.IsFullscreenAppInFront(_hwnd);
        if (hide != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = hide;
            if (hide) Hide();
            else Show();
        }
        if (_hiddenForFullscreen) return;

        Reposition();
        NativeMethods.KeepOnTop(_hwnd);
    }

    private string DescribePlot(int index)
    {
        var plot = Engine.Plots[index];
        if (!Crops.TryGet(plot.CropId, out var crop))
            return $"Plot {index + 1}: empty\nClick to plant something";

        if (GameEngine.IsRipe(plot))
            return $"{crop.Name}: ready!\nClick to harvest (+{GameEngine.HandHarvestFortuneBonus} Harvest Fortune)";

        return $"{crop.Name}: ripe in {Format.Duration(GameEngine.SecondsUntilRipe(plot))}\n"
               + "Click to tend it (grows faster) · Right-click to change crop";
    }

    // ================================================================== clicks & menus

    private void OnPlotClicked(int index, FrameworkElement cell)
    {
        if (index >= Engine.Plots.Count) return;
        var plot = Engine.Plots[index];

        if (plot.CropId is null)
        {
            OpenPlotMenu(index, cell);
        }
        else if (GameEngine.IsRipe(plot))
        {
            Engine.Harvest(index, handHarvest: true); // OnHarvested shows the numbers
        }
        else if (Engine.Tend(index))
        {
            var sparkle = SpriteImage(_host.Sprites["sparkle"], 5 * _unit);
            AddFloater(sparkle, PlotLeft(index) + _tile / 2, _tileTop + 4 * _unit, life: 0.6, rise: 6 * _unit);
        }
    }

    private void OnLockClicked(double x)
    {
        int plotNumber = Engine.Plots.Count + 1;
        if (Engine.TryUnlockNextPlot(out string reason))
        {
            string crop = Crops.Get(Engine.Plots[^1].CropId!).Name;
            QueueToast($"PLOT {plotNumber} UNLOCKED", $"Planted with {crop}. Right-click it to change.", GardenBrush, 4);
            _host.Save();
        }
        else
        {
            AddTextFloater(reason, x + _tile / 2, _tileTop, DangerBrush, life: 1.6);
        }
    }

    private void OpenPlotMenu(int index, FrameworkElement target)
    {
        if (index >= Engine.Plots.Count) return;
        var menu = CropMenu.Create(_host, $"Plot {index + 1}: choose a crop", Engine.Plots[index].CropId,
            crop => Engine.Plant(index, crop.Id));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Plant on every plot...", () => OpenPlantAllMenu(target)));
        menu.Items.Add(MakeItem("Open garden", TogglePanel));
        ShowMenu(menu, target);
    }

    private void OpenPlantAllMenu(FrameworkElement target)
    {
        var menu = CropMenu.Create(_host, "Plant on every plot", null, crop => Engine.PlantAll(crop.Id));
        ShowMenu(menu, target);
    }

    private void OpenMainMenu(FrameworkElement target)
    {
        var menu = new ContextMenu();
        var open = MakeItem(_panel is null ? "Open garden" : "Close garden", TogglePanel);
        open.FontWeight = FontWeights.Bold;
        menu.Items.Add(open);

        long value = Engine.InventoryValue();
        var sell = MakeItem($"Sell all crops (+{Format.Number(value)} coins)", () => Engine.SellAll());
        sell.IsEnabled = value > 0;
        menu.Items.Add(sell);
        menu.Items.Add(new Separator());

        menu.Items.Add(CheckItem("Always show the garden", Settings.StripMode == StripMode.Always,
            () => Settings.StripMode = StripMode.Always));
        menu.Items.Add(CheckItem("Only show it when I hover", Settings.StripMode == StripMode.OnHover,
            () => Settings.StripMode = StripMode.OnHover));

        var size = new MenuItem { Header = "Size" };
        foreach (var option in Enum.GetValues<StripSize>())
            size.Items.Add(CheckItem(option.ToString(), Settings.Size == option, () => Settings.Size = option));
        menu.Items.Add(size);

        var position = new MenuItem { Header = "Position" };
        foreach (var option in Enum.GetValues<StripAnchor>())
            position.Items.Add(CheckItem(option.ToString(), Settings.Anchor == option, () => Settings.Anchor = option));
        menu.Items.Add(position);

        menu.Items.Add(CheckItem("Farmer harvests by themselves", Settings.FarmerAutoHarvest,
            () => Settings.FarmerAutoHarvest = !Settings.FarmerAutoHarvest));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Quit TaskBarGame", () => ((App)Application.Current).Quit()));
        ShowMenu(menu, target);
    }

    private static MenuItem MakeItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private MenuItem CheckItem(string header, bool isChecked, Action apply)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) =>
        {
            apply();
            // Wait for the menu to close before rebuilding the strip underneath it.
            Dispatcher.BeginInvoke(_host.ApplySettings);
        };
        return item;
    }

    private void ShowMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Top;
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (HoverMode && !ShouldStayExpanded()) _collapseTimer.Start();
        };
        menu.IsOpen = true;
    }

    // ================================================================== the big panel

    public void TogglePanel()
    {
        if (_panel is not null)
        {
            _panel.Close();
            return;
        }

        _panel = new PanelWindow(_host);
        _panel.Closed += (_, _) =>
        {
            _panel = null;
            if (HoverMode && !ShouldStayExpanded()) _collapseTimer.Start();
        };

        var area = SystemParameters.WorkArea;
        double stripScreenLeft = Left + _stripLeft;
        double left = Settings.Anchor switch
        {
            StripAnchor.Right => stripScreenLeft + _stripWidth - _panel.Width,
            StripAnchor.Center => stripScreenLeft + (_stripWidth - _panel.Width) / 2,
            _ => stripScreenLeft,
        };
        _panel.Left = Math.Max(area.Left, Math.Min(left, area.Right - _panel.Width));
        _panel.Top = Math.Max(area.Top, Top + PopupSpace - _panel.Height - 8);
        _panel.Show();
        _panel.Activate();
    }

    // ================================================================== floating numbers

    private void OnHarvested(HarvestResult result)
    {
        if (!_expanded || _hiddenForFullscreen || result.PlotIndex >= _plots.Count) return;

        SpawnBreakParticles(result.PlotIndex, result.Crop);
        if (!Settings.ShowFloatingText) return;

        double centerX = PlotLeft(result.PlotIndex) + _tile / 2;
        AddTextFloater($"+{result.Drops} {result.Crop.Name}", centerX, _tileTop - _unit, CropBrush(result.Crop));
        if (result.CoinsFromAutoSell > 0)
            AddTextFloater($"+{Format.Number(result.CoinsFromAutoSell)} coins", centerX, _tileTop + _fontSize, GoldBrush, delay: 0.25);
        if (result.HandHarvest)
            AddTextFloater($"+{result.FarmingXp} Farming XP", centerX, _tileTop + _fontSize, XpBrush, delay: 0.15);
    }

    private void AddTextFloater(string text, double centerX, double y, Brush brush, double life = 1.3, double delay = 0)
    {
        if (_unit <= 0) return;
        var label = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontWeight = FontWeights.Bold,
            FontSize = _fontSize,
            Effect = TextShadow,
            IsHitTestVisible = false,
        };
        AddFloater(label, centerX, y, life, rise: 7 * _unit + _fontSize, delay);
    }

    private void AddFloater(FrameworkElement element, double centerX, double y, double life, double rise, double delay = 0)
    {
        if (_unit <= 0) return;
        element.IsHitTestVisible = false;
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = element.DesiredSize.Width;
        double x = Math.Clamp(centerX - width / 2, 0, Math.Max(0, Width - width));
        element.Opacity = 0;
        Place(_fxLayer, element, x, y);
        _floaters.Add(new Floater(element, y, rise, life, -delay));
    }

    private void UpdateFloaters(double dt)
    {
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.Age += dt;
            if (f.Age >= f.Life)
            {
                _fxLayer.Children.Remove(f.Element);
                _floaters.RemoveAt(i);
                continue;
            }
            if (f.Age < 0) continue;

            double t = f.Age / f.Life;
            double eased = 1 - (1 - t) * (1 - t);
            Canvas.SetTop(f.Element, f.StartY - f.Rise * eased);
            f.Element.Opacity = t < 0.65 ? 1 : 1 - (t - 0.65) / 0.35;
        }
    }

    // ================================================================== crop break particles

    /// <summary>Little bits of crop that pop out and fall when a crop is broken.</summary>
    private void SpawnBreakParticles(int plotIndex, CropDef crop)
    {
        if (_unit <= 0) return;
        var brushes = _host.Sprites.ParticleBrushes(crop);
        if (brushes.Count == 0) return;

        double left = PlotLeft(plotIndex);
        for (int i = 0; i < 10; i++)
        {
            double size = _rng.NextDouble() < 0.4 ? 2 * _unit : _unit;
            var shape = new Rectangle
            {
                Width = size,
                Height = size,
                Fill = brushes[_rng.Next(brushes.Count)],
                IsHitTestVisible = false,
            };
            var particle = new Particle(shape)
            {
                X = left + _tile * (0.2 + 0.6 * _rng.NextDouble()),
                Y = _tileTop + _tile * (0.25 + 0.5 * _rng.NextDouble()),
                Vx = (_rng.NextDouble() * 2 - 1) * 14 * _unit,
                Vy = -(10 + _rng.NextDouble() * 18) * _unit,
                Life = 0.45 + _rng.NextDouble() * 0.3,
            };
            Place(_fxLayer, shape, particle.X, particle.Y);
            _particles.Add(particle);
        }
    }

    private void UpdateParticles(double dt)
    {
        double gravity = 120 * _unit;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _fxLayer.Children.Remove(p.Shape);
                _particles.RemoveAt(i);
                continue;
            }
            p.Vy += gravity * dt;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            Canvas.SetLeft(p.Shape, p.X);
            Canvas.SetTop(p.Shape, p.Y);
            double t = p.Age / p.Life;
            p.Shape.Opacity = t < 0.6 ? 1 : 1 - (t - 0.6) / 0.4;
        }
    }

    // ================================================================== popups (toasts)

    private void OnLeveledUp(LevelUp levelUp)
    {
        string title = levelUp.Skill == SkillKind.Farming
            ? $"FARMING LEVEL {levelUp.NewLevel}!"
            : $"GARDEN LEVEL {levelUp.NewLevel}!";
        var brush = levelUp.Skill == SkillKind.Farming ? XpBrush : GardenBrush;
        string body = levelUp.Rewards.Count > 0 ? string.Join(" · ", levelUp.Rewards) : "Keep farming!";
        QueueToast(title, body, brush, 4.5);
    }

    private void OnCollectionTier(CollectionTierUp tierUp)
    {
        QueueToast(
            $"{tierUp.Crop.Name.ToUpperInvariant()} COLLECTION {Format.Roman(tierUp.NewTier)}",
            $"+{CollectionTiers.FortunePerTier} {tierUp.Crop.Name} Fortune · +{CollectionTiers.GardenXpPerTier * tierUp.NewTier} Garden XP",
            CropBrush(tierUp.Crop), 4);
    }

    public void ShowOfflineReport(OfflineReport report)
    {
        long crops = report.Drops.Values.Sum();
        string body = $"Your farmer harvested {Format.Number(report.Harvests)} times while you were away "
                      + $"({Format.Duration(report.Away.TotalSeconds)}): +{Format.Number(crops)} crops, "
                      + $"+{Format.Number(report.FarmingXp)} Farming XP.";
        if (report.FarmingLevelAfter > report.FarmingLevelBefore)
            body += $" Farming {report.FarmingLevelBefore} → {report.FarmingLevelAfter}!";
        if (report.GardenLevelAfter > report.GardenLevelBefore)
            body += $" Garden {report.GardenLevelBefore} → {report.GardenLevelAfter}!";
        QueueToast("WELCOME BACK!", body, GoldBrush, 9);
    }

    private void QueueToast(string title, string body, Brush accent, double seconds)
    {
        _toastQueue.Enqueue(new Toast(title, body, accent, seconds));
        if (_toast is null) NextToast();
    }

    private void NextToast()
    {
        if (_toast?.Element is { } old) _fxLayer.Children.Remove(old);
        _toast = _toastQueue.Count > 0 ? _toastQueue.Dequeue() : null;
        if (_toast is not null) ShowToastElement(_toast);
    }

    private void ShowToastElement(Toast toast)
    {
        if (_unit <= 0) return; // shown by BuildLayout once the strip exists
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = toast.Title,
            Foreground = toast.Accent,
            FontWeight = FontWeights.Bold,
            FontSize = _fontSize + 1,
        });
        text.Children.Add(new TextBlock
        {
            Text = toast.Body,
            Foreground = TextBrush,
            FontSize = _fontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var border = new Border
        {
            Background = ToastBrush,
            BorderBrush = toast.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 7),
            MaxWidth = Math.Min(Width - 8, 340),
            Child = text,
            Cursor = Cursors.Hand,
            ToolTip = "Click to dismiss",
        };
        border.MouseLeftButtonUp += (_, e) => { NextToast(); e.Handled = true; };
        border.Measure(new Size(border.MaxWidth, double.PositiveInfinity));

        double width = border.DesiredSize.Width;
        double x = Math.Clamp(_stripLeft + (_stripWidth - width) / 2, 0, Math.Max(0, Width - width));
        double y = Math.Max(0, PopupSpace - border.DesiredSize.Height - 4 * _unit - _fontSize);
        Place(_fxLayer, border, x, y);
        toast.Element = border;
        if (toast.Age == 0) border.Opacity = 0;
    }

    private void UpdateToast(double dt)
    {
        if (_toast?.Element is not { } element) return;
        _toast.Age += dt;
        if (_toast.Age >= _toast.Seconds)
        {
            NextToast();
            return;
        }
        double fadeIn = Math.Min(1, _toast.Age / 0.25);
        double fadeOut = Math.Min(1, (_toast.Seconds - _toast.Age) / 0.5);
        element.Opacity = Math.Min(fadeIn, fadeOut);
    }

    // ================================================================== small helpers

    private static readonly Dictionary<string, Brush> CropBrushes = new();

    private static Brush CropBrush(CropDef crop)
    {
        if (!CropBrushes.TryGetValue(crop.Id, out var brush))
            CropBrushes[crop.Id] = brush = Format.Brush(crop.ColorHex);
        return brush;
    }

    private static Effect CreateTextShadow()
    {
        var shadow = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 2, Opacity = 0.9 };
        shadow.Freeze();
        return shadow;
    }

    private sealed record PlotView(int Index, double X, Image Crop, Image Sparkle, Rectangle Progress, TextBlock Tip);

    private sealed class Floater(FrameworkElement element, double startY, double rise, double life, double age)
    {
        public FrameworkElement Element { get; } = element;
        public double StartY { get; } = startY;
        public double Rise { get; } = rise;
        public double Life { get; } = life;
        public double Age { get; set; } = age;
    }

    private sealed class Particle(Rectangle shape)
    {
        public Rectangle Shape { get; } = shape;
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Age { get; set; }
        public double Life { get; set; }
    }

    private sealed class Toast(string title, string body, Brush accent, double seconds)
    {
        public string Title { get; } = title;
        public string Body { get; } = body;
        public Brush Accent { get; } = accent;
        public double Seconds { get; } = seconds;
        public double Age { get; set; }
        public FrameworkElement? Element { get; set; }
    }
}
