using System.Globalization;

namespace TaskBarGame.Core;

/// <summary>A small pixel-art image. Pixels are 0xAARRGGBB, row by row from the top.</summary>
public sealed record Sprite(string Name, int Width, int Height, uint[] Pixels);

/// <summary>
/// Reads pixel art from a text file (see Assets/sprites.txt). The format is:
/// <code>
/// [palette]
/// G = 4a9e3f        one character = one colour
/// . = transparent
///
/// [sprite wheat_ripe]
/// ...GG...           one character per pixel, every row the same width
/// </code>
/// Lines starting with # are comments.
/// </summary>
public sealed class SpriteSheet
{
    private readonly Dictionary<string, Sprite> _sprites;

    private SpriteSheet(Dictionary<string, Sprite> sprites, Dictionary<char, uint> palette)
    {
        _sprites = sprites;
        Palette = palette;
    }

    /// <summary>The colours from [palette], by character (0xAARRGGBB).</summary>
    public IReadOnlyDictionary<char, uint> Palette { get; }

    public IReadOnlyCollection<Sprite> All => _sprites.Values;

    public Sprite this[string name] =>
        _sprites.TryGetValue(name, out var sprite)
            ? sprite
            : throw new KeyNotFoundException($"No sprite called '{name}' in sprites.txt.");

    public bool Contains(string name) => _sprites.ContainsKey(name);

    /// <summary>Loads the sprites.txt that is built into TaskBarGame.Core.</summary>
    public static SpriteSheet LoadDefault()
    {
        var assembly = typeof(SpriteSheet).Assembly;
        using var stream = assembly.GetManifestResourceStream("TaskBarGame.Core.Assets.sprites.txt")
            ?? throw new InvalidOperationException("sprites.txt is missing from TaskBarGame.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static SpriteSheet Parse(string text)
    {
        var palette = new Dictionary<char, uint> { ['.'] = 0 };
        var sprites = new Dictionary<string, Sprite>();

        string? section = null;
        string? spriteName = null;
        var rows = new List<string>();
        int lineNumber = 0;

        void FinishSprite()
        {
            if (spriteName is null) return;
            if (rows.Count == 0) throw Error($"Sprite '{spriteName}' has no pixels.");
            int width = rows[0].Length;
            var pixels = new uint[width * rows.Count];
            for (int y = 0; y < rows.Count; y++)
            {
                if (rows[y].Length != width)
                    throw Error($"Sprite '{spriteName}' row {y + 1} is {rows[y].Length} wide, expected {width}.");
                for (int x = 0; x < width; x++)
                {
                    char c = rows[y][x];
                    if (!palette.TryGetValue(c, out var colour))
                        throw Error($"Sprite '{spriteName}' uses '{c}' which isn't in the palette.");
                    pixels[y * width + x] = colour;
                }
            }
            if (!sprites.TryAdd(spriteName, new Sprite(spriteName, width, rows.Count, pixels)))
                throw Error($"Sprite '{spriteName}' is defined twice.");
            spriteName = null;
            rows.Clear();
        }

        FormatException Error(string message) => new($"sprites.txt line {lineNumber}: {message}");

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.TrimEnd('\r', ' ', '\t');
            string trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                FinishSprite();
                string header = trimmed[1..^1].Trim();
                if (header == "palette")
                {
                    section = "palette";
                }
                else if (header.StartsWith("sprite ", StringComparison.Ordinal))
                {
                    section = "sprite";
                    spriteName = header["sprite ".Length..].Trim();
                }
                else
                {
                    throw Error($"Unknown section [{header}].");
                }
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (section == "sprite") FinishSprite();
                continue;
            }

            switch (section)
            {
                case "palette":
                    palette[ParsePaletteKey(trimmed, Error)] = ParsePaletteColour(trimmed, Error);
                    break;
                case "sprite" when spriteName is not null:
                    rows.Add(trimmed);
                    break;
                default:
                    throw Error("Pixels must come after a [sprite name] header.");
            }
        }
        FinishSprite();
        return new SpriteSheet(sprites, palette);
    }

    private static char ParsePaletteKey(string line, Func<string, FormatException> error)
    {
        int eq = line.IndexOf('=');
        string key = eq >= 0 ? line[..eq].Trim() : "";
        if (key.Length != 1) throw error("Palette lines look like 'G = 4a9e3f'.");
        return key[0];
    }

    private static uint ParsePaletteColour(string line, Func<string, FormatException> error)
    {
        string value = line[(line.IndexOf('=') + 1)..].Trim();
        int comment = value.IndexOf(' ');
        if (comment >= 0) value = value[..comment];
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return 0;

        value = value.TrimStart('#');
        if (value.Length == 6) value = "ff" + value;
        if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            throw error($"'{value}' isn't a colour. Use rrggbb or aarrggbb.");
        return argb;
    }
}
