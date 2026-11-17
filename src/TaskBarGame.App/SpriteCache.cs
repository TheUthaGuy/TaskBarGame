using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskBarGame.Core;

namespace TaskBarGame.App;

/// <summary>Turns sprites from sprites.txt into WPF images (once each) and remembers them.</summary>
public sealed class SpriteCache
{
    // Light and dark colour for each tier, as 0xAARRGGBB. Index 0 = not owned.
    private static readonly (uint Light, uint Dark)[] HoeColours =
    {
        (0xFF8A8A8A, 0xFF4A4A4A), // none (shown greyed out)
        (0xFFA39A8E, 0xFF6B645B), // Basic: flint
        (0xFFE2E6EA, 0xFF8D949B), // Reinforced: iron
        (0xFFFFD84A, 0xFFC08A10), // Masterwork: gold
    };

    private static readonly (uint Light, uint Dark)[] ArmourColours =
    {
        (0xFF8A8A8A, 0xFF4A4A4A), // none
        (0xFFF2D36B, 0xFFB8892A), // Straw
        (0xFFF39A3C, 0xFFB0561A), // Harvester
        (0xFF6FE08F, 0xFF1F8A4A), // Verdant
    };

    private static readonly Dictionary<TalismanFamily, (uint Light, uint Dark)> TalismanColours = new()
    {
        [TalismanFamily.Clover] = (0xFF7FE08A, 0xFF2F9A4A),
        [TalismanFamily.Swiftstep] = (0xFF8EE8FF, 0xFF2A8AB8),
        [TalismanFamily.Sprout] = (0xFFD4F06A, 0xFF6A9A1A),
        [TalismanFamily.Scholar] = (0xFFC9A0FF, 0xFF6A3FB0),
    };

    private readonly SpriteSheet _sheet;
    private readonly Dictionary<string, BitmapSource> _images = new();
    private readonly Dictionary<string, IReadOnlyList<Brush>> _particles = new();

    public SpriteCache(SpriteSheet sheet) => _sheet = sheet;

    public BitmapSource this[string name]
    {
        get
        {
            if (_images.TryGetValue(name, out var image)) return image;
            image = ToBitmap(_sheet[name], _sheet[name].Pixels);
            _images[name] = image;
            return image;
        }
    }

    public BitmapSource CropIcon(CropDef crop) => this[crop.RipeSprite];

    /// <summary>A sack for packed crops, a crate for dense ones, nothing for plain crops.</summary>
    public BitmapSource? ItemContainer(ItemDef item) => item.Kind switch
    {
        ItemKind.Packed => this["sack"],
        ItemKind.Dense => this["crate"],
        _ => null,
    };

    /// <summary>
    /// A farmer frame holding the hoe of the given tier (0 = no hoe: empty hands).
    /// The hoe in sprites.txt uses N/n for the head and b for the handle.
    /// </summary>
    public BitmapSource FarmerWithHoe(string frame, int tier)
    {
        var map = tier <= 0
            ? new Dictionary<char, uint> { ['N'] = 0, ['n'] = 0, ['b'] = 0 }
            : new Dictionary<char, uint> { ['N'] = HoeColours[tier].Light, ['n'] = HoeColours[tier].Dark };
        return Recolored(frame, $"hoe{tier}", map);
    }

    public BitmapSource ToolIcon(int tier)
    {
        var (light, dark) = HoeColours[Math.Clamp(tier, 0, Tools.MaxTier)];
        return Recolored("icon_hoe", $"t{tier}", new Dictionary<char, uint> { ['I'] = light, ['i'] = dark });
    }

    public BitmapSource ArmourIcon(ArmourSlot slot, int tier)
    {
        var (light, dark) = ArmourColours[Math.Clamp(tier, 0, Armour.MaxTier)];
        return Recolored("icon_" + slot.ToString().ToLowerInvariant(), $"t{tier}",
            new Dictionary<char, uint> { ['A'] = light, ['a'] = dark });
    }

    /// <summary>Charm, Ring or Relic shape, in the talisman's own colour (tier 0 shows a grey charm).</summary>
    public BitmapSource TalismanIcon(TalismanFamily family, int tier)
    {
        string shape = tier switch { 2 => "icon_ring", 3 => "icon_relic", _ => "icon_charm" };
        var (light, dark) = tier <= 0 ? (0xFF8A8A8Au, 0xFF4A4A4Au) : TalismanColours[family];
        return Recolored(shape, $"{family}{tier}", new Dictionary<char, uint> { ['E'] = light, ['Z'] = dark });
    }

    /// <summary>
    /// Colours for the little bits that fly out when a crop breaks: one brush per coloured pixel
    /// of the grown crop, so picking one at random matches the crop's look.
    /// </summary>
    public IReadOnlyList<Brush> ParticleBrushes(CropDef crop)
    {
        if (_particles.TryGetValue(crop.Id, out var brushes)) return brushes;

        var byColour = new Dictionary<uint, Brush>();
        var list = new List<Brush>();
        foreach (uint argb in _sheet[crop.RipeSprite].Pixels)
        {
            if (argb >> 24 == 0) continue;
            if (!byColour.TryGetValue(argb, out var brush))
            {
                brush = new SolidColorBrush(ToColor(argb));
                brush.Freeze();
                byColour[argb] = brush;
            }
            list.Add(brush);
        }
        _particles[crop.Id] = list;
        return list;
    }

    /// <summary>A copy of a sprite with some palette colours swapped (0 = transparent).</summary>
    private BitmapSource Recolored(string name, string variant, IReadOnlyDictionary<char, uint> map)
    {
        string key = name + "#" + variant;
        if (_images.TryGetValue(key, out var image)) return image;

        var swaps = new Dictionary<uint, uint>();
        foreach (var (letter, colour) in map)
            if (_sheet.Palette.TryGetValue(letter, out var original)) swaps[original] = colour;

        var sprite = _sheet[name];
        var pixels = (uint[])sprite.Pixels.Clone();
        for (int i = 0; i < pixels.Length; i++)
            if (swaps.TryGetValue(pixels[i], out var swapped)) pixels[i] = swapped;

        image = ToBitmap(sprite, pixels);
        _images[key] = image;
        return image;
    }

    private static BitmapSource ToBitmap(Sprite sprite, uint[] argb)
    {
        // Sprite pixels are 0xAARRGGBB, which is exactly Bgra32's byte order in memory.
        var pixels = new int[argb.Length];
        Buffer.BlockCopy(argb, 0, pixels, 0, pixels.Length * sizeof(int));
        var image = BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Bgra32, null,
            pixels, sprite.Width * sizeof(int));
        image.Freeze();
        return image;
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
