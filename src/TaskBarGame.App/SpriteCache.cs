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
        [TalismanFamily.Merchant] = (0xFFFFE066, 0xFFB8860B),
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

    /// <summary>Shirt, overalls and overalls-shadow colours for each outfit (see Cosmetics.Outfits).</summary>
    private static readonly Dictionary<string, (uint Shirt, uint Overalls, uint OverallsDark)> OutfitColours = new()
    {
        ["classic"] = (0xFFE04A3C, 0xFF4A7BD1, 0xFF2C4F8F),
        ["harvest"] = (0xFFF39A3C, 0xFF9C7048, 0xFF6E4A30),
        ["meadow"] = (0xFFF5F1E6, 0xFF4A9E3F, 0xFF2E6B2E),
        ["twilight"] = (0xFFC9A0FF, 0xFF4B3F8C, 0xFF2E2660),
        ["golden"] = (0xFFFFF0A0, 0xFFE8B923, 0xFFA87B16),
        ["wanderer"] = (0xFF59C7EE, 0xFF6E6E6E, 0xFF444444),
    };

    /// <summary>
    /// A farmer frame in a hat and outfit. <paramref name="hoeTier"/> 0 or more = empty hands (the
    /// strip draws the tool separately); -1 = the frame as drawn. Other hats replace the straw hat in sprites.txt
    /// (h/H/e) and are lined up with its band, which moves down a pixel on some walking frames.
    /// </summary>
    public BitmapSource Farmer(string frame, int hoeTier, string hat, string outfit)
    {
        string key = $"{frame}#hoe{hoeTier}#{hat}#{outfit}";
        if (_images.TryGetValue(key, out var image)) return image;

        var sprite = _sheet[frame];
        var swaps = new Dictionary<uint, uint>();
        void Swap(char letter, uint colour)
        {
            if (_sheet.Palette.TryGetValue(letter, out var original)) swaps[original] = colour;
        }

        if (hoeTier >= 0)
        {
            // Take the little hoe drawn in sprites.txt out of the farmer's hands: the strip draws
            // the crop's real tool picture there instead.
            Swap('N', 0);
            Swap('n', 0);
            Swap('b', 0);
        }
        var (shirt, overalls, overallsDark) = OutfitColours.TryGetValue(outfit, out var colours) ? colours : OutfitColours["classic"];
        Swap('R', shirt);
        Swap('U', overalls);
        Swap('u', overallsDark);

        bool newHat = hat != Cosmetics.DefaultHat && _sheet.Contains("hat_" + hat);
        var hatColours = new HashSet<uint>();
        foreach (char letter in "hHe")
            if (_sheet.Palette.TryGetValue(letter, out var c)) hatColours.Add(c);
        uint band = _sheet.Palette.GetValueOrDefault('e');

        var pixels = (uint[])sprite.Pixels.Clone();
        int bandRow = -1;
        for (int y = 0; y < sprite.Height && bandRow < 0; y++)
            for (int x = 0; x < sprite.Width; x++)
                if (sprite.Pixels[y * sprite.Width + x] == band) { bandRow = y; break; }

        for (int i = 0; i < pixels.Length; i++)
        {
            uint original = sprite.Pixels[i];
            if (newHat && hatColours.Contains(original)) pixels[i] = 0;          // take off the straw hat
            else if (swaps.TryGetValue(original, out var swapped)) pixels[i] = swapped;
        }

        if (newHat)
        {
            var hatSprite = _sheet["hat_" + hat];
            int offset = bandRow < 0 ? 0 : bandRow - 4;
            for (int y = 0; y < hatSprite.Height; y++)
            for (int x = 0; x < hatSprite.Width && x < sprite.Width; x++)
            {
                uint colour = hatSprite.Pixels[y * hatSprite.Width + x];
                int row = y + offset;
                if (colour >> 24 == 0 || row < 0 || row >= sprite.Height) continue;
                pixels[row * sprite.Width + x] = colour;
            }
        }

        image = ToBitmap(sprite, pixels);
        _images[key] = image;
        return image;
    }

    /// <summary>The four blue shades every tool picture's blade is drawn with (the Masterwork look).</summary>
    private static readonly uint[] BladeBlues = { 0xFF1D6F6A, 0xFF36B3A8, 0xFF7FE6DA, 0xFFE6FFFB };

    /// <summary>The blade's shades for each tier, dark to light: none (faded), Basic iron, Reinforced gold, Masterwork blue.</summary>
    private static readonly uint[][] BladeColours =
    {
        new uint[] { 0xFF4A4A4A, 0xFF6E6E6E, 0xFF8E8E8E, 0xFFB0B0B0 },
        new uint[] { 0xFF4A4F57, 0xFF8A929C, 0xFFC9CED6, 0xFFF4F6F8 },
        new uint[] { 0xFF8A5A0A, 0xFFD99A1C, 0xFFFFD84A, 0xFFFFF6C8 },
        BladeBlues,
    };

    /// <summary>
    /// A crop's tool picture (from Assets/Tools) with its blade in the tier's colour: iron, gold or
    /// blue. Tier 0 (not crafted yet) is shown faded and grey.
    /// </summary>
    public BitmapSource ToolIcon(CropDef crop, int tier)
    {
        tier = Math.Clamp(tier, 0, Tools.MaxTier);
        string key = $"tool:{crop.Id}#{tier}";
        if (_images.TryGetValue(key, out var image)) return image;

        var source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Tools/{Tools.Texture(crop)}.png"));
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        var pixels = new uint[width * height];
        converted.CopyPixels(pixels, width * 4, 0);

        var swaps = new Dictionary<uint, uint>();
        for (int i = 0; i < BladeBlues.Length; i++) swaps[BladeBlues[i]] = BladeColours[tier][i];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (swaps.TryGetValue(pixels[i], out var swapped)) pixels[i] = swapped;
            else if (tier == 0 && pixels[i] >> 24 != 0) pixels[i] = Grey(pixels[i]);
            if (tier == 0 && pixels[i] >> 24 != 0) pixels[i] = (pixels[i] & 0x00FFFFFF) | 0x99000000; // faded
        }

        image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        _images[key] = image;
        return image;
    }

    private static uint Grey(uint argb)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint y = (r * 30 + g * 59 + b * 11) / 100;
        return (argb & 0xFF000000) | (y << 16) | (y << 8) | y;
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

    /// <summary>The Compactor: grey when you don't have one, iron for the Compactor, purple for the Dense Compactor.</summary>
    public BitmapSource CompactorIcon(int tier)
    {
        var (light, dark) = tier switch
        {
            1 => (0xFFB0B0B0u, 0xFF6A6A6Au),
            2 => (0xFFA97FDCu, 0xFF6E4597u),
            _ => (0xFF8A8A8Au, 0xFF4A4A4Au),
        };
        return Recolored("icon_compactor", $"t{tier}", new Dictionary<char, uint> { ['I'] = light, ['i'] = dark });
    }

    /// <summary>A visitor in their own shirt and hair colours.</summary>
    public BitmapSource Visitor(VisitorDef visitor)
    {
        static uint Hex(string hex) => 0xFF000000 | Convert.ToUInt32(hex, 16);
        return Recolored("visitor", visitor.Id, new Dictionary<char, uint>
        {
            ['T'] = Hex(visitor.Shirt),
            ['j'] = Hex(visitor.ShirtDark),
            ['Q'] = Hex(visitor.Hair),
        });
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
