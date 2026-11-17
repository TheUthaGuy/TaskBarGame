using System;
using System.Windows;
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

    public PanelWindow(GameHost host)
    {
        _host = host;
        InitializeComponent();

        _vm = new PanelViewModel(host);
        DataContext = _vm;

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
