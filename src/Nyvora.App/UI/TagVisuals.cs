using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nyvora.Core.Tasks;
using Windows.UI;

namespace Nyvora.App.UI;

/// <summary>Shared look of a tag: palette (indexed by <see cref="TagDef.Color"/>) and the small chip.</summary>
internal static class TagVisuals
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA), // blue
        Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80), // green
        Color.FromArgb(0xFF, 0xFB, 0x92, 0x3C), // orange
        Color.FromArgb(0xFF, 0xF4, 0x72, 0xB6), // pink
        Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA), // violet
        Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF), // teal
    };

    public static Color Of(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];

    public static Border Chip(TagDef tag)
    {
        var c = Of(tag.Color);
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B)),
            Child = new TextBlock { Text = "#" + tag.Name, FontSize = 11, Foreground = new SolidColorBrush(c) },
        };
    }
}
