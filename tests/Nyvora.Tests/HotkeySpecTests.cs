using Nyvora.Core.Settings;
using Xunit;

namespace Nyvora.Tests;

public class HotkeySpecTests
{
    [Fact]
    public void DefaultIsCtrlAltN()
    {
        Assert.Equal("Ctrl+Alt+N", HotkeySpec.Default.ToString());
        Assert.True(HotkeySpec.Default.IsValid);
        Assert.Equal(3u, (uint)HotkeySpec.Default.Modifiers); // MOD_ALT | MOD_CONTROL, as Win32 expects
        Assert.Equal(0x4E, HotkeySpec.Default.Key);
    }

    [Theory]
    [InlineData("Ctrl+Alt+N")]
    [InlineData("ctrl + shift + f5")]
    [InlineData("Win+Q")]
    [InlineData("Alt+1")]
    [InlineData("Control+Windows+F12")]
    public void ParsesAndRoundTrips(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));
        Assert.True(HotkeySpec.TryParse(spec.ToString(), out var again));
        Assert.Equal(spec, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N")]              // no modifier
    [InlineData("Shift+N")]        // Shift alone would hijack typing
    [InlineData("Ctrl+")]          // no key
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+Delete")]    // unsupported key
    [InlineData("Ctrl+Tab")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+A+B")]       // two keys
    [InlineData("Ctrl+Alt+?")]
    public void RejectsInvalid(string text)
    {
        Assert.False(HotkeySpec.TryParse(text, out _));
    }

    [Fact]
    public void FormatsModifiersInAFixedOrder()
    {
        var spec = new HotkeySpec(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control, 'K');
        Assert.Equal("Ctrl+Alt+Shift+Win+K", spec.ToString());
    }

    [Fact]
    public void FKeysUseTheirNumbers()
    {
        Assert.True(HotkeySpec.TryParse("Ctrl+F1", out var f1));
        Assert.Equal(0x70, f1.Key);
        Assert.Equal("Ctrl+F1", f1.ToString());
        Assert.True(HotkeySpec.TryParse("Alt+F12", out var f12));
        Assert.Equal(0x7B, f12.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Shift+N")]
    public void BadSettingsFallBackToTheDefault(string? saved)
    {
        Assert.Equal(HotkeySpec.Default, HotkeySpec.FromSettings(saved));
    }

    [Fact]
    public void SavedShortcutIsUsed()
    {
        Assert.Equal(new HotkeySpec(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'K'), HotkeySpec.FromSettings("Ctrl+Shift+K"));
    }
}
