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
