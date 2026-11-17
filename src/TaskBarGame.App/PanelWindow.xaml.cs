using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TaskBarGame.App.ViewModels;

namespace TaskBarGame.App;

/// <summary>The garden panel. Most of the work happens in <see cref="PanelViewModel"/>.</summary>
public partial class PanelWindow : Window
{
    private readonly GameHost _host;
    private readonly PanelViewModel _vm;
    private readonly DispatcherTimer _refreshTimer;

    public PanelWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();

        _vm = new PanelViewModel(host);
        DataContext = _vm;

        // Growth timers and progress bars tick along while the panel is open.
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
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
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

    private void ChangeCrop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlotRow row } button) return;
        if (row.Index >= _host.Engine.Plots.Count) return;

        var menu = CropMenu.Create(_host, $"Plot {row.Index + 1}: choose a crop", _host.Engine.Plots[row.Index].CropId,
            crop =>
            {
                _host.Engine.Plant(row.Index, crop.Id);
                _vm.Refresh();
            });
        ShowMenuUnder(menu, button);
    }

    private void PlantAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button) return;
        var menu = CropMenu.Create(_host, "Plant on every plot", null, crop =>
        {
            _host.Engine.PlantAll(crop.Id);
            _vm.Refresh();
        });
        ShowMenuUnder(menu, button);
    }

    private static void ShowMenuUnder(System.Windows.Controls.ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Start your garden over from scratch?\n\nYou'll lose all coins, levels, crops and plots. Your settings are kept.",
            "Reset progress", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _host.Engine.ResetProgress();
        _host.Save();
        _vm.Refresh();
    }
}
