namespace Nyvora.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,

    // Same values as Win32 MOD_ALT / MOD_CONTROL / MOD_SHIFT / MOD_WIN, so they can be passed straight to RegisterHotKey.
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global shortcut: modifiers plus one key. <see cref="Key"/> is a Windows virtual-key code, which for A-Z and 0-9
/// equals the character itself and for F1-F12 is 0x70-0x7B.
/// </summary>
public readonly record struct HotkeySpec(HotkeyModifiers Modifiers, int Key)
{
    public static readonly HotkeySpec Default = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'N');

    /// <summary>
    /// Needs Ctrl, Alt or Win (Shift alone would hijack ordinary typing), and a letter, digit or F-key
    /// (so Delete, Tab, Esc and friends can never be taken over).
    /// </summary>
    public bool IsValid =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0 && IsSupportedKey(Key);

    public static bool IsSupportedKey(int key) =>
        key is >= 'A' and <= 'Z' || key is >= '0' and <= '9' || key is >= 0x70 and <= 0x7B;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key is >= 0x70 and <= 0x7B ? "F" + (Key - 0x70 + 1) : ((char)Key).ToString());
        return string.Join("+", parts);
    }

    /// <summary>Reads text like "Ctrl+Alt+N" or "ctrl + shift + f5"; anything that is not a valid shortcut fails.</summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var mods = HotkeyModifiers.None;
        var key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToUpperInvariant();
            switch (part)
            {
                case "CTRL" or "CONTROL":
                    mods |= HotkeyModifiers.Control;
                    continue;
                case "ALT":
                    mods |= HotkeyModifiers.Alt;
                    continue;
                case "SHIFT":
                    mods |= HotkeyModifiers.Shift;
                    continue;
                case "WIN" or "WINDOWS":
                    mods |= HotkeyModifiers.Win;
                    continue;
            }

            if (key != 0)
            {
                return false; // two keys
            }

            if (part.Length == 1)
            {
                key = part[0];
            }
            else if (part[0] == 'F' && int.TryParse(part.AsSpan(1), out var n) && n is >= 1 and <= 12)
            {
                key = 0x70 + n - 1;
            }
            else
            {
                return false;
            }
        }

        var parsed = new HotkeySpec(mods, key);
        if (!parsed.IsValid)
        {
            return false;
        }

        spec = parsed;
        return true;
    }

    /// <summary>The saved shortcut, or the default when the text is missing or invalid.</summary>
    public static HotkeySpec FromSettings(string? text) => TryParse(text, out var s) ? s : Default;
}
