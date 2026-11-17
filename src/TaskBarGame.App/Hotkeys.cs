using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TaskBarGame.App.Interop;

namespace TaskBarGame.App;

/// <summary>A keyboard shortcut like Ctrl+Alt+F, as typed in the settings.</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    /// <summary>"Ctrl+Alt+F".</summary>
    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>Reads "Ctrl+Alt+F" (any order, any case). Null for "" or anything that isn't a usable shortcut.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key is not null || ParseKey(raw) is not { } parsed) return null;
                    key = parsed;
                    break;
            }
        }
        return key is { } k && IsUsable(modifiers, k) ? new Hotkey(modifiers, k) : null;
    }

    /// <summary>
    /// A shortcut needs Ctrl, Alt or Win (so it doesn't swallow normal typing) and a key that
    /// isn't itself a modifier.
    /// </summary>
    public static bool IsUsable(ModifierKeys modifiers, Key key) =>
        (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0
        && key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? NativeMethods.MOD_CONTROL : 0)
        | (Modifiers.HasFlag(ModifierKeys.Alt) ? NativeMethods.MOD_ALT : 0)
        | (Modifiers.HasFlag(ModifierKeys.Shift) ? NativeMethods.MOD_SHIFT : 0)
        | (Modifiers.HasFlag(ModifierKeys.Windows) ? NativeMethods.MOD_WIN : 0);

    public uint NativeKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
        _ => key.ToString(),
    };

    private static Key? ParseKey(string text)
    {
        if (text.Length == 1 && char.IsDigit(text[0])) return Key.D0 + (text[0] - '0');
        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && text.Length == 4 && char.IsDigit(text[3]))
            return Key.NumPad0 + (text[3] - '0');
        return Enum.TryParse<Key>(text, ignoreCase: true, out var key) && Enum.IsDefined(key) ? key : null;
    }
}
