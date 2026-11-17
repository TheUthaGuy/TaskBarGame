using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using TaskBarGame.Core;

namespace TaskBarGame.App;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class Format
{
    public static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>"1 coin", "5 coins".</summary>
    public static string Coins(long value) => value == 1 ? "1 coin" : $"{Number(value)} coins";

    public static string Number(double value, int decimals) =>
        value.ToString("N" + decimals, CultureInfo.CurrentCulture);

    /// <summary>45s, 1m 30s, 2h 5m.</summary>
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds)));
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return t.Seconds == 0 ? $"{t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    public static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    public static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Builds the "pick a crop" menu used by the strip and the panel.</summary>
internal static class CropMenu
{
    public static ContextMenu Create(GameHost host, string title, string? currentCropId, Action<CropDef> onPick)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (var crop in Crops.All)
        {
            bool unlocked = host.Engine.IsCropUnlocked(crop);
            var item = new MenuItem
            {
                Header = unlocked ? crop.Name : $"{crop.Name}  (Garden Level {crop.UnlockGardenLevel})",
                Icon = PixelImage(host.Sprites.CropIcon(crop), 16),
                IsEnabled = unlocked,
                FontWeight = crop.Id == currentCropId ? FontWeights.Bold : FontWeights.Normal,
            };
            item.Click += (_, _) => onPick(crop);
            menu.Items.Add(item);
        }
        return menu;
    }

    public static Image PixelImage(ImageSource source, double size)
    {
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }
}

/// <summary>Adds or removes TaskBarGame from the programs Windows starts when you log in.</summary>
internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskBarGame Harvest Edition";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is string exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
