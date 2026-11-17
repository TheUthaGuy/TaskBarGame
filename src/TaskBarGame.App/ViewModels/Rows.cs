using System.Windows.Input;
using System.Windows.Media;
using TaskBarGame.Core;

namespace TaskBarGame.App.ViewModels;

/// <summary>One plot in the Garden tab.</summary>
public sealed class PlotRow : ObservableObject
{
    private static readonly Brush RipeBrush = Format.Brush("#F5C542");

    private ImageSource? _icon;
    private string _cropName = "";
    private string _status = "";
    private double _progress;
    private Brush _progressBrush = RipeBrush;

    public PlotRow(int index) => Index = index;

    public int Index { get; }
    public string Title => $"Plot {Index + 1} · ";
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public string CropName { get => _cropName; private set => Set(ref _cropName, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public Brush ProgressBrush { get => _progressBrush; private set => Set(ref _progressBrush, value); }

    public void Update(GameHost host)
    {
        var plot = host.Engine.Plots[Index];
        Icon = host.Sprites.ForPlot(plot) ?? host.Sprites["soil"];
        Progress = GameEngine.GrowthProgress(plot);

        if (!Crops.TryGet(plot.CropId, out var crop))
        {
            CropName = "Empty";
            Status = "";
            return;
        }
        CropName = crop.Name;
        bool ripe = GameEngine.IsRipe(plot);
        Status = ripe ? "Ready!" : Format.Duration(GameEngine.SecondsUntilRipe(plot));
        ProgressBrush = ripe ? RipeBrush : CropBrushes.Get(crop);
    }
}

/// <summary>One crop in the Farming tab's crop guide.</summary>
public sealed class CropGuideRow : ObservableObject
{
    private static readonly Brush Gold = Format.Brush("#F5C542");
    private static readonly Brush Muted = Format.Brush("#A3B19F");

    private string _details = "";
    private string _badge = "";
    private Brush _badgeBrush = Muted;
    private double _rowOpacity = 1;

    public CropGuideRow(CropDef crop, ImageSource icon)
    {
        Crop = crop;
        Icon = icon;
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public string Details { get => _details; private set => Set(ref _details, value); }
    public string Badge { get => _badge; private set => Set(ref _badge, value); }
    public Brush BadgeBrush { get => _badgeBrush; private set => Set(ref _badgeBrush, value); }
    public double RowOpacity { get => _rowOpacity; private set => Set(ref _rowOpacity, value); }

    public void Update(GameEngine engine)
    {
        double drops = engine.ExpectedDrops(Crop, handHarvest: false);
        Details = $"Grows in {Format.Duration(Crop.GrowSeconds)} · {Format.Number(drops, 1)} per harvest · {Crop.FarmingXp} Farming XP";

        bool unlocked = engine.IsCropUnlocked(Crop);
        Badge = unlocked ? $"{Crop.SellPrice} coins each" : $"Garden Level {Crop.UnlockGardenLevel}";
        BadgeBrush = unlocked ? Gold : Muted;
        RowOpacity = unlocked ? 1 : 0.5;
    }
}

/// <summary>One crop in the Collections tab.</summary>
public sealed class CollectionRow : ObservableObject
{
    private string _tierText = "";
    private string _countText = "";
    private string _rewardText = "";
    private double _progress;
    private double _rowOpacity = 1;

    public CollectionRow(CropDef crop, ImageSource icon)
    {
        Crop = crop;
        Icon = icon;
        ProgressBrush = CropBrushes.Get(crop);
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public Brush ProgressBrush { get; }
    public string TierText { get => _tierText; private set => Set(ref _tierText, value); }
    public string CountText { get => _countText; private set => Set(ref _countText, value); }
    public string RewardText { get => _rewardText; private set => Set(ref _rewardText, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public double RowOpacity { get => _rowOpacity; private set => Set(ref _rowOpacity, value); }

    public void Update(GameEngine engine)
    {
        long collected = engine.GetCollection(Crop);
        int tier = CollectionTiers.TierFor(Crop, collected);
        var thresholds = CollectionTiers.Thresholds(Crop);
        long? next = CollectionTiers.NextThreshold(Crop, collected);
        long previous = tier == 0 ? 0 : thresholds[tier - 1];

        TierText = tier == 0 ? "Not started"
            : next is null ? $"Tier {Format.Roman(tier)} · MAXED"
            : $"Tier {Format.Roman(tier)}";
        CountText = next is null
            ? $"{Format.Number(collected)} collected"
            : $"{Format.Number(collected)} / {Format.Number(next.Value)}";
        Progress = next is null ? 1 : (double)(collected - previous) / (next.Value - previous);
        RewardText = tier == 0
            ? $"Tier I: +{CollectionTiers.FortunePerTier} {Crop.Name} Fortune"
            : $"+{tier * CollectionTiers.FortunePerTier} {Crop.Name} Fortune";
        RowOpacity = engine.IsCropUnlocked(Crop) || collected > 0 ? 1 : 0.45;
    }
}

/// <summary>One stack of crops in the Market tab.</summary>
public sealed class InventoryRow : ObservableObject
{
    private string _countText = "";
    private string _sellText = "";

    public InventoryRow(CropDef crop, ImageSource icon, ICommand sell)
    {
        Crop = crop;
        Icon = icon;
        SellCommand = sell;
    }

    public CropDef Crop { get; }
    public ImageSource Icon { get; }
    public string Name => Crop.Name;
    public ICommand SellCommand { get; }
    public string CountText { get => _countText; private set => Set(ref _countText, value); }
    public string SellText { get => _sellText; private set => Set(ref _sellText, value); }

    public void Update(GameEngine engine)
    {
        long count = engine.GetInventory(Crop);
        CountText = $"x{Format.Number(count)} · {Crop.SellPrice} coins each";
        SellText = $"Sell for {Format.Number(count * Crop.SellPrice)}";
    }
}

/// <summary>Shared, cached brushes in each crop's colour.</summary>
internal static class CropBrushes
{
    private static readonly System.Collections.Generic.Dictionary<string, Brush> ByCrop = new();

    public static Brush Get(CropDef crop)
    {
        if (!ByCrop.TryGetValue(crop.Id, out var brush))
            ByCrop[crop.Id] = brush = Format.Brush(crop.ColorHex);
        return brush;
    }
}
