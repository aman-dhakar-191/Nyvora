using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Nyvora.App.Services;
using Nyvora.Core.Animation;
using WinRT.Interop;
using Windows.Graphics;
using static Nyvora.App.Windowing.NativeMethods;

namespace Nyvora.App.Windowing;

/// <summary>
/// Owns everything native about the pill window: borderless popup styling, no taskbar entry,
/// no-activate behaviour while resting, top-center placement, DPI, rounded clipping and
/// reacting to display changes.
/// </summary>
internal sealed class PillWindowHost : IDisposable
{
    private const int TopMarginDip = 8;
    private const int MaxCornerRadiusDip = 24;

    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly SubclassProc _subclass; // field keeps the delegate alive for native callbacks
    private SizeD _size = PillAnimator.RestingSize;

    public PillWindowHost(Window window, bool alwaysOnTop)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            // Topmost keeps the pill above normal windows. Exclusive-fullscreen apps and games
            // are not affected by this flag (documented limitation).
            p.IsAlwaysOnTop = alwaysOnTop;
            p.SetBorderAndTitleBar(false, false);
        }

        // WS_POPUP drops caption/frame (and the minimum tracking size that comes with them);
        // WS_EX_TOOLWINDOW hides the taskbar button and Alt+Tab entry.
        var style = (long)GetWindowLongPtr(_hwnd, GWL_STYLE);
        style = (style & ~0x00CF0000L /* caption|sysmenu|thickframe|min|max */) | WS_POPUP | WS_VISIBLE;
        SetWindowLongPtr(_hwnd, GWL_STYLE, (nint)style);
        SetNoActivate(true);

        _subclass = OnMessage;
        SetWindowSubclass(_hwnd, _subclass, 1, 0);
    }

    public void SetAlwaysOnTop(bool value)
    {
        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = value;
        }
    }

    public double Scale => GetDpiForWindow(_hwnd) / 96.0;

    public void Show()
    {
        Apply(_size);
        _appWindow.Show(false); // activateWindow: false -> do not steal focus on launch
    }

    /// <summary>While resting the window never takes activation; an expanded pill must be able to receive keys.</summary>
    public void SetNoActivate(bool noActivate)
    {
        var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        ex = noActivate ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>Gives the pill real keyboard focus (needed for typing); only call for user-initiated interaction.</summary>
    public void Focus()
    {
        SetNoActivate(false);
        _window.Activate();
        SetForegroundWindow(_hwnd);
    }

    private bool _buttonWasDown;

    /// <summary>
    /// True once per mouse-button press that lands outside the pill. Polled instead of relying on
    /// Deactivated, which never fires for a window that was shown without activation or whose
    /// click-away target does not take foreground (e.g. the desktop or another topmost window).
    /// </summary>
    public bool PollClickOutside()
    {
        var down = IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON);
        var pressed = down && !_buttonWasDown;
        _buttonWasDown = down;
        if (!pressed || !GetCursorPos(out var p) || !GetWindowRect(_hwnd, out var r))
        {
            return false;
        }

        return p.X < r.Left || p.X >= r.Right || p.Y < r.Top || p.Y >= r.Bottom;
    }

    private static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    public void ResetClickTracking() => _buttonWasDown = true; // ignore a press already in progress

    /// <summary>Resizes (in DIPs) around the top-center anchor and re-clips to a rounded shape.</summary>
    public void Apply(SizeD sizeDip)
    {
        _size = sizeDip;
        var scale = Scale;
        var area = DisplayArea.Primary.OuterBounds;

        var w = Math.Max(1, (int)Math.Round(sizeDip.Width * scale));
        var h = Math.Max(1, (int)Math.Round(sizeDip.Height * scale));
        var x = area.X + (area.Width - w) / 2;
        var y = area.Y + (int)Math.Round(TopMarginDip * scale);

        _appWindow.MoveAndResize(new RectInt32(x, y, w, h));

        var radius = (int)Math.Round(Math.Min(sizeDip.Height / 2, MaxCornerRadiusDip) * scale);
        // The system takes ownership of the region handle after a successful SetWindowRgn.
        SetWindowRgn(_hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2), true);
    }

    private nint OnMessage(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg is WM_DPICHANGED or WM_DISPLAYCHANGE or WM_SETTINGCHANGE)
        {
            // Re-run placement against the (possibly new) primary display and DPI.
            _window.DispatcherQueue.TryEnqueue(() => Apply(_size));
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        RemoveWindowSubclass(_hwnd, _subclass, 1);
        Log.Info("Window host disposed");
    }
}
