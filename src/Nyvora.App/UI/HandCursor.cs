using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Nyvora.App.UI;

// WinUI 3 only exposes the cursor through a protected property, so clickable controls
// opt in by subclassing.

public sealed class HandButton : Button
{
    public HandButton() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
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
