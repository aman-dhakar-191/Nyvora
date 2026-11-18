using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nyvora.Core.Timesheet;

namespace Nyvora.App.UI;

/// <summary>
/// The daily check-in: a small window that asks what you did today. Built in code (no XAML) because it is one plain form.
/// <paramref name="submit"/> gets the entry and whether to fill Keka; it returns a problem to show, or null to close.
/// </summary>
internal sealed class CheckInWindow : Window
{
    private readonly TextBox _hours = new() { Header = "Hours", PlaceholderText = "8", Width = 160, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _comment = new()
    {
        Header = "What did you do? It becomes the task and the comment in Keka.",
        FontSize = 16,
        PlaceholderText = "What did you do?",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 240,
    };

    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFB, 0x71, 0x85)), Visibility = Visibility.Collapsed };
    private readonly DateOnly _date;
    private readonly Func<CheckInEntry, bool, string?> _submit;

    public CheckInWindow(DateOnly date, CheckInEntry? today, double defaultHours, string? cannotFillBecause, Func<CheckInEntry, bool, string?> submit)
    {
        _date = date;
        _submit = submit;
        Title = "Nyvora daily check-in";

        // Today's entry if there is one (to edit it), otherwise the fixed hours from Settings.
        _hours.Text = (today?.Hours ?? defaultHours).ToString("0.##", CultureInfo.InvariantCulture);
        _comment.Text = today?.Comment ?? "";

        var canFill = cannotFillBecause is null;
        var big = new Thickness(22, 10, 22, 10);
        var save = new Button { Content = "Save", MinWidth = 90, Padding = big, FontSize = 16 };
        save.Click += (_, _) => Submit(fill: false);
        var fill = new Button { Content = "Save and fill Keka", IsEnabled = canFill, Padding = big, FontSize = 16 };

        fill.Click += (_, _) => Submit(fill: true);
        var later = new Button { Content = "Not now", Padding = big, FontSize = 16 };
        later.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(save);
        buttons.Children.Add(fill);
        buttons.Children.Add(later);

        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(36, 30, 36, 30) };
        panel.Children.Add(new TextBlock
        {
            Text = $"What did you do on {date.ToString("dddd, d MMMM", CultureInfo.CurrentCulture)}?",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_comment);
        panel.Children.Add(_hours);
        panel.Children.Add(_problem);
        panel.Children.Add(buttons);
        if (cannotFillBecause is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "To fill Keka: " + cannotFillBecause,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
        }

        Content = new ScrollViewer
        {
            Content = panel,
            RequestedTheme = ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1B, 0x1B, 0x22)),
        };

        // Size in device-independent units (so it is the same size on a scaled display), centred on the screen.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = Math.Min((int)(700 * scale), work.Width - 40);
        var h = Math.Min((int)(720 * scale), work.Height - 40);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true; // so it is seen even when another app is in front
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void Submit(bool fill)
    {
        var text = _hours.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
        {
            Show("Hours must be a number, like 8 or 7.5.");
            return;
        }

        var entry = new CheckInEntry
        {
            Date = _date,
            Hours = hours,
            Comment = _comment.Text.Trim(),
        };
        if (_submit(entry, fill) is { } problem)
        {
            Show(problem);
            return;
        }

        Close();
    }

    private void Show(string message)
    {
        _problem.Text = message;
        _problem.Visibility = Visibility.Visible;
    }
}
