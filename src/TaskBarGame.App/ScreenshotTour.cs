using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>
/// Plays a demo field and saves screenshots of the strip and every panel tab, then quits.
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

    /// <summary>A field a few hours in, with crops grown so the farmer has a row to sweep.</summary>
    public static GameState CreateDemoState()
    {
        var state = GameState.CreateNew();
        state.GardenXp = 60_000;                               // Garden 8
        state.FarmingXp = FarmingSkill.TotalXpFor(18) - 2;     // the first crop broken levels up
        state.Coins = 48_250;

        // About an hour and a half of Bazaar prices, and a couple of orders waiting.
        var market = new GameEngine(state, new Random(4));
        state.Inventory["packed_carrot"] = 20;
        market.Tick(90 * 60);
        market.CreateSellOffer("packed_carrot", 6, market.BazaarBuyPrice(Items.Packed(Crops.Carrot)) + 20, out _);
        market.CreateBuyOrder("packed_melon", 8, market.BazaarSellPrice(Items.Packed(Crops.Melon)) - 30, out _);

        state.Field.CropId = "wheat";
        state.Field.Length = 16;
        state.Field.Tiles.Clear();
        double[] growth = { 1, 1, 1, 0.2, 1, 1, 0.5, 1, 1, 1, 0.8, 1, 1, 0.1, 1, 1 };
        foreach (double g in growth) state.Field.Tiles.Add(g * Crops.Wheat.RegrowSeconds);

        state.Tools["wheat"] = new ToolState { Tier = 2, Breaks = 7_420 };
        state.Tools["carrot"] = new ToolState { Tier = 1, Breaks = 2_310 };
        state.Tools["potato"] = new ToolState { Tier = 1, Breaks = 1_150 };
        foreach (var slot in Armour.Slots) state.Armour[slot] = 1;
        state.Armour[ArmourSlot.Helmet] = 2;
        state.Talismans[TalismanFamily.Clover] = 2;
        state.Talismans[TalismanFamily.Swiftstep] = 1;
        state.Talismans[TalismanFamily.Scholar] = 1;
        state.Compactor = 1;
        state.Settings.AutoSellCrops.Add("potato");
        state.Settings.LockedItems.Add("packed_melon");
        state.Settings.CompactorSkips.Add("carrot");

        state.Collections["wheat"] = 52_000;
        state.Collections["carrot"] = 30_000;
        state.Collections["potato"] = 20_000;
        state.Collections["sugarcane"] = 4_000;
        state.Collections["pumpkin"] = 900;
        state.Collections["melon"] = 2_500;
        state.Collections["cocoa"] = 50;
        state.Inventory["wheat"] = 340;
        state.Inventory["packed_wheat"] = 6;
        state.Inventory["dense_wheat"] = 1;
        state.Inventory["carrot"] = 120;
        state.Inventory["potato"] = 95;
        state.Inventory["packed_melon"] = 9;
        state.Inventory["sugarcane"] = 75; // enough for a Sugar Cane Hoe: shows "Upgrades 1"
        state.Stats.TotalBreaks = 18_730;
        state.Stats.HandBreaks = 412;
        state.Stats.CoinsEarned = 96_420;
        state.Stats.ItemsCrafted = 10;

        state.Settings.HideInFullscreen = false;
        state.LastSavedUtc = DateTime.UtcNow;
        return state;
    }

    public async Task RunAsync()
    {
        Directory.CreateDirectory(_folder);

        // Let the farmer get going (so a "+N" has floated up), then catch the moment a crop breaks.
        await Wait(1.6);
        await NextHarvest(TimeSpan.FromSeconds(6));
        await Wait(0.1);
        SaveStrip("strip.png");

        // ...and the moment they teleport from the end of the row back to the start.
        await NextTeleport(TimeSpan.FromSeconds(10));
        await Wait(FarmerAgent.TeleportDuration * 0.75);
        SaveStrip("strip-teleport.png");

        _strip.TogglePanel();
        await Wait(0.8);
        var panel = _strip.Panel ?? throw new InvalidOperationException("The garden panel didn't open.");
        string[] tabs = { "field", "farming", "upgrades", "collections", "bazaar", "inventory", "settings" };
        string[] alsoScrolledDown = { "farming", "upgrades", "inventory", "settings" };
        for (int i = 0; i < tabs.Length; i++)
        {
            panel.Tabs.SelectedIndex = i;
            await Wait(0.5);
            SaveElement((FrameworkElement)panel.Content, $"panel-{tabs[i]}.png");

            // ...and the bottom of the longer tabs (the crop guide, the Compactor, auto-sell, pop-ups).
            if (Array.IndexOf(alsoScrolledDown, tabs[i]) >= 0 && panel.Tabs.SelectedContent is ScrollViewer scroller)
            {
                scroller.ScrollToEnd();
                await Wait(0.4);
                SaveElement((FrameworkElement)panel.Content, $"panel-{tabs[i]}-bottom.png");
                scroller.ScrollToHome();
            }
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
        void OnHarvest(BreakResult _) => done.TrySetResult();
        _host.Engine.Broke += OnHarvest;
        return Task.WhenAny(done.Task, Task.Delay(timeout))
            .ContinueWith(_ => _host.Engine.Broke -= OnHarvest, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private Task NextTeleport(TimeSpan timeout)
    {
        var done = new TaskCompletionSource();
        void OnTeleport() => done.TrySetResult();
        _host.Farmer.Teleported += OnTeleport;
        return Task.WhenAny(done.Task, Task.Delay(timeout))
            .ContinueWith(_ => _host.Farmer.Teleported -= OnTeleport, TaskScheduler.FromCurrentSynchronizationContext());
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
