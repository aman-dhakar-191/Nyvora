# Architecture

| Project | Responsibility |
|---|---|
| `Nyvora.Core` (net8.0) | `PillStateMachine` (Resting/Expanding/Expanded/Collapsing) and `PillAnimator` (pure size interpolation and easing). No UI or Windows dependencies, so it is unit-tested anywhere. |
| `Nyvora.App` (WinUI 3, unpackaged, self-contained Windows App SDK) | `App`/`MainWindow` wire the state machine, a `DispatcherQueueTimer` animation loop and the XAML content. `Windowing/PillWindowHost` owns native window behavior. |
| `Nyvora.Tests` | xunit tests for Core. |

## State machine

Valid transitions: `Resting→Expanding`, `Collapsing→Expanding`, `Expanding→Collapsing`, `Expanded→Collapsing`, plus completion `Expanding→Expanded` and `Collapsing→Resting`. Anything else is rejected and leaves state unchanged. Each animated transition gets a new *generation*; completion with a stale generation is ignored, so an interrupted animation can never move the machine. Animations start from the currently displayed size, so reversing is continuous.

## Native window decisions (`PillWindowHost`)

- **Borderless:** `WS_POPUP` style with caption/frame bits removed; presenter border and title bar disabled.
- **No taskbar/Alt+Tab:** `WS_EX_TOOLWINDOW`.
- **No focus stealing:** `WS_EX_NOACTIVATE` while resting and `Show(activate:false)` at launch. It is lifted when the user expands the pill so Esc works, and restored when it returns to rest. Clicking outside the pill collapses it (polled mouse press); losing focus alone does not, so screenshot tools and Alt+Tab leave it open.
- **Z-order:** `IsAlwaysOnTop`. This does not cover exclusive-fullscreen apps/games; no fullscreen detection yet.
- **Click-through:** the window is exactly the pill's size, so it never covers unrelated desktop content; no transparent hit-test region exists.
- **Rounded shape:** `SetWindowRgn` with a round-rect region updated every frame. Works on Windows 10 and 11 but edges are not anti-aliased. Fallback/alternative: layered window or composition-based shape (future).
- **DPI/display changes:** layout is in DIPs and converted with `GetDpiForWindow`; a window subclass re-applies placement on `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`.
