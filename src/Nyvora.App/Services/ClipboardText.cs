using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using static Nyvora.App.Windowing.NativeMethods;

namespace Nyvora.App.Services;

/// <summary>
/// Makes Ctrl+V and the context-menu Paste work in single-line text boxes by reading the clipboard through
/// Win32 (CF_UNICODETEXT). That path does not depend on this tool-style window being the foreground window,
/// which the WinUI clipboard API can require. Non-text clipboard content falls through to the default handling.
/// </summary>
internal static class ClipboardText
{
    public static void Attach(TextBox box) => box.Paste += OnPaste;

    private static void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        var box = (TextBox)sender;
        var text = TryRead();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Single line: fold line breaks, then respect MaxLength like typing would.
        text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        if (box.MaxLength > 0)
        {
            var room = box.MaxLength - (box.Text.Length - box.SelectionLength);
            if (room <= 0)
            {
                e.Handled = true;
                return;
            }

            if (text.Length > room)
            {
                text = text[..room];
            }
        }

        e.Handled = true;
        var start = box.SelectionStart;
        box.SelectedText = text;
        box.SelectionStart = start + text.Length;
        box.SelectionLength = 0;
    }

    private static string? TryRead()
    {
        // Another process can hold the clipboard open for a moment; a few short retries is the documented approach.
        var opened = false;
        for (var attempt = 0; attempt < 4 && !opened; attempt++)
        {
            opened = OpenClipboard(0);
            if (!opened)
            {
                Thread.Sleep(10);
            }
        }

        if (!opened)
        {
            Log.Error($"Paste: could not open the clipboard (Win32 error {Marshal.GetLastWin32Error()})");
            return null;
        }

        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                return null;
            }

            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
            {
                return null;
            }

            var ptr = GlobalLock(handle);
            if (ptr == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
