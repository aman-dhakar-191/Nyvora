using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nyvora.App.UI;

// WinUI 3 only exposes the cursor through a protected property, so clickable controls
// opt in by subclassing.

public sealed class HandButton : Button
{
    public HandButton()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        // Compact by default (the stock button is 32 px tall with generous padding); callers can still override.
        MinHeight = 28;
        MinWidth = 0;
        Padding = new Thickness(10, 4, 10, 4);
        // App.xaml sets 8 px corners for every control, but the stock Button ignored it and drew square corners next to
        // the rounded text boxes. Setting it here makes every button in the app match.
        CornerRadius = new CornerRadius(8);
    }

    /// <summary>For icon buttons with no background of their own: a disabled one fades to its dimmed icon instead of showing a dark block.</summary>
    public HandButton Quiet()
    {
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Resources["ButtonBackgroundDisabled"] = clear;
        Resources["ButtonBorderBrushDisabled"] = clear;
        return this;
    }
}

public sealed class HandCheckBox : CheckBox
{
    public HandCheckBox() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}

public sealed class ResizeGrip : Grid
{
    public ResizeGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
}

public sealed class HandGrid : Grid
{
    public HandGrid() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
