using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>Turns sprites from sprites.txt into WPF images (once each) and remembers them.</summary>
public sealed class SpriteCache
{
    private readonly SpriteSheet _sheet;
    private readonly Dictionary<string, BitmapSource> _images = new();

    public SpriteCache(SpriteSheet sheet) => _sheet = sheet;

    public BitmapSource this[string name]
    {
        get
        {
            if (_images.TryGetValue(name, out var image)) return image;

            var sprite = _sheet[name];
            // Sprite pixels are 0xAARRGGBB, which is exactly Bgra32's byte order in memory.
            var pixels = new int[sprite.Pixels.Length];
            Buffer.BlockCopy(sprite.Pixels, 0, pixels, 0, pixels.Length * sizeof(int));

            image = BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Bgra32, null,
                pixels, sprite.Width * sizeof(int));
            image.Freeze();
            _images[name] = image;
            return image;
        }
    }

    public BitmapSource CropIcon(CropDef crop) => this[crop.RipeSprite];

    /// <summary>The picture for a plot's crop at its current growth stage, or null if empty.</summary>
    public BitmapSource? ForPlot(PlotState plot)
    {
        if (!Crops.TryGet(plot.CropId, out var crop)) return null;
        return GameEngine.GrowthStage(plot) switch
        {
            0 => this["stage0"],
            1 => this["stage1"],
            2 => this["stage2"],
            _ => this[crop.RipeSprite],
        };
    }
}
