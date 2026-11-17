using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>
/// Plays a demo garden and saves screenshots of the strip and every panel tab, then quits.
/// Run with: TaskBarGame.exe --screenshots path\to\folder
/// (Used to make the screenshots in docs/screenshots. Your real save file is never touched.)
/// </summary>
internal sealed class ScreenshotTour
{
    private readonly GameHost _host;
    private readonly StripWindow _strip;
    private readonly string _folder;

    public ScreenshotTour(GameHost host, StripWindow strip, string folder)
    {
        _host = host;
        _strip = strip;
        _folder = Path.GetFullPath(folder);
    }

    /// <summary>The folder after "--screenshots", or null when the game should start normally.</summary>
    public static string? OutputFolder(string[] args)
    {
        int index = Array.IndexOf(args, "--screenshots");
        if (index < 0) return null;
        return index + 1 < args.Length ? args[index + 1] : "screenshots";
    }

    /// <summary>A garden a few hours in, with some crops ripe so the farmer has work to do.</summary>
    public static GameState CreateDemoState()
    {
        var state = GameState.CreateNew();
        state.GardenXp = 2_600;                                // Garden 7, halfway to 8
        state.FarmingXp = FarmingSkill.TotalXpFor(13) - 3;     // the first harvest levels up
        state.Coins = 12_345;

        (string Crop, double Progress)[] plots =
        {
            ("wheat", 1), ("carrot", 0.7), ("potato", 1), ("sugarcane", 0.4),
            ("pumpkin", 1), ("melon", 0.85), ("wheat", 0.1), ("carrot", 1),
        };
        state.Plots.Clear();
        foreach (var (crop, progress) in plots)
            state.Plots.Add(new PlotState { CropId = crop, Growth = Crops.Get(crop).GrowSeconds * progress });

        state.Collections["wheat"] = 6_500;
        state.Collections["carrot"] = 3_000;
        state.Collections["potato"] = 900;
        state.Collections["sugarcane"] = 300;
        state.Collections["pumpkin"] = 60;
        state.Collections["melon"] = 40;
        state.Inventory["wheat"] = 412;
        state.Inventory["carrot"] = 230;
        state.Inventory["potato"] = 96;
        state.Inventory["pumpkin"] = 18;
        state.Stats.TotalHarvests = 1_873;
        state.Stats.HandHarvests = 214;
        state.Stats.CoinsEarned = 18_420;

        state.Settings.HideInFullscreen = false;
        state.LastSavedUtc = DateTime.UtcNow;
        return state;
    }

    public async Task RunAsync()
    {
        Directory.CreateDirectory(_folder);

        // Let the farmer get going, then catch the moment just after a crop breaks.
        await Wait(1.4);
        await NextHarvest(TimeSpan.FromSeconds(6));
        await Wait(0.15);
        SaveStrip("strip.png");

        _strip.TogglePanel();
        await Wait(0.8);
        var panel = _strip.Panel ?? throw new InvalidOperationException("The garden panel didn't open.");
        string[] tabs = { "garden", "farming", "collections", "market", "settings" };
        for (int i = 0; i < tabs.Length; i++)
        {
            panel.Tabs.SelectedIndex = i;
            await Wait(0.5);
            SaveElement((FrameworkElement)panel.Content, $"panel-{tabs[i]}.png");
        }
        panel.Close();

        // "Only show it when I hover" mode: just the farmer and the ripe-crop badge.
        _host.Settings.StripMode = StripMode.OnHover;
        _host.ApplySettings();
        await Wait(1.0);
        SaveStrip("strip-hover-mode.png");
    }

    private static Task Wait(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

    private Task NextHarvest(TimeSpan timeout)
    {
        var done = new TaskCompletionSource();
        void OnHarvest(HarvestResult _) => done.TrySetResult();
        _host.Engine.Harvested += OnHarvest;
        return Task.WhenAny(done.Task, Task.Delay(timeout))
            .ContinueWith(_ => _host.Engine.Harvested -= OnHarvest, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Draws the strip (and anything floating above it) trimmed to the parts you can see.</summary>
    private void SaveStrip(string name)
    {
        var image = Render(_strip.Root);
        Save(TrimToContent(image, padding: 12), name);
    }

    /// <summary>Draws a window's contents at 2x size, keeping see-through parts see-through.</summary>
    private void SaveElement(FrameworkElement element, string name) => Save(Render(element), name);

    private static BitmapSource Render(FrameworkElement element)
    {
        const double scale = 2;
        element.UpdateLayout();
        var image = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(element);
        image.Freeze();
        return image;
    }

    /// <summary>Crops away fully see-through edges.</summary>
    private static BitmapSource TrimToContent(BitmapSource image, int padding)
    {
        int width = image.PixelWidth, height = image.PixelHeight;
        var pixels = new uint[width * height];
        image.CopyPixels(pixels, width * 4, 0);

        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (pixels[y * width + x] >> 24 == 0) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        if (right < 0) return image;

        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(width - 1, right + padding);
        bottom = Math.Min(height - 1, bottom + padding);
        return new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
    }

    private void Save(BitmapSource image, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(_folder, name));
        encoder.Save(file);
    }
}
