using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TaskBarGame.App.ViewModels;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>The panel. Most of the work happens in <see cref="PanelViewModel"/>.</summary>
public partial class PanelWindow : Window
{
    private readonly GameHost _host;
    private readonly PanelViewModel _vm;
    private readonly DispatcherTimer _refreshTimer;
    private bool _shown;

    public PanelWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();

        _vm = new PanelViewModel(host);
        DataContext = _vm;

        // Open on the tab you had open last time, and remember where you drag the panel.
        Tabs.SelectedIndex = Math.Clamp(host.Settings.PanelTab, 0, Tabs.Items.Count - 1);
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == Tabs) _host.Settings.PanelTab = Tabs.SelectedIndex;
        };
        ContentRendered += (_, _) => _shown = true;
        LocationChanged += (_, _) =>
        {
            if (!_shown) return; // still being placed
            _host.Settings.PanelLeft = Left;
            _host.Settings.PanelTop = Top;
        };
        ChartHost.SizeChanged += (_, e) => _vm.ChartWidth = e.NewSize.Width;

        // Timers and progress bars tick along while the panel is open.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => _vm.Refresh();
        _refreshTimer.Start();

        _host.Engine.Changed += OnGameChanged;
        _host.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _host.Engine.Changed -= OnGameChanged;
            _host.SettingsChanged -= OnSettingsChanged;
            _host.Save();
        };

        PreviewKeyDown += (_, e) =>
        {
            // Waiting for a new keyboard shortcut in Settings: the keys go there.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_vm.OnCaptureKey(key, Keyboard.Modifiers))
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) => _host.HotkeysPaused = false;
    }

    private void OnGameChanged() => _vm.Refresh();

    private void OnSettingsChanged()
    {
        _vm.RaiseSettings();
        _vm.Refresh();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Switches to a tab by its name ("VisitorsTab").</summary>
    public void ShowTab(string name)
    {
        if (FindName(name) is TabItem tab) Tabs.SelectedItem = tab;
    }

    /// <summary>The Composter's Add ▾ button: a menu of items to put in (all of it).</summary>
    private void ComposterAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Put all of it in the Composter", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var (item, count) in _vm.ComposterChoices)
        {
            var entry = new MenuItem { Header = $"{item.Name} (x{Format.Number(count)})" };
            entry.Click += (_, _) => _vm.AddToComposter(item);
            menu.Items.Add(entry);
        }
        if (menu.Items.Count == 2) menu.Items.Add(new MenuItem { Header = "Nothing to put in (locked items stay out)", IsEnabled = false });
        menu.IsOpen = true;
    }

    /// <summary>The Pack ▾ button: a menu of how many to pack.</summary>
    private void Pack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InventoryRow row } button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Pack how many?", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var (label, count) in row.PackChoices)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => _vm.Pack(row.Item, count);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Start the Harvest Edition over from scratch?\n\nYou'll lose all coins, levels, crops and upgrades. Your settings are kept.",
            "Reset progress", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _host.Engine.ResetProgress();
        _host.Save();
        _vm.Refresh();
    }
}
