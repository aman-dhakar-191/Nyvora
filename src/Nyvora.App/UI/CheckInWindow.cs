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
    private readonly TextBox _hours = new() { Header = "Hours (fixed in Settings; change it for a short day)", PlaceholderText = "8", Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _comment = new()
    {
        Header = "What did you do? (this is the task and the comment in Keka)",
        PlaceholderText = "What did you do?",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 160,
    };

    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFB, 0x71, 0x85)), Visibility = Visibility.Collapsed };
    private readonly DateOnly _date;
    private readonly Func<CheckInEntry, bool, string?> _submit;

    public CheckInWindow(DateOnly date, CheckInEntry? today, double defaultHours, bool canFill, Func<CheckInEntry, bool, string?> submit)
    {
        _date = date;
        _submit = submit;
        Title = "Nyvora daily check-in";

        // Today's entry if there is one (to edit it), otherwise the fixed hours from Settings.
        _hours.Text = (today?.Hours ?? defaultHours).ToString("0.##", CultureInfo.InvariantCulture);
        _comment.Text = today?.Comment ?? "";

        var save = new Button { Content = "Save", MinWidth = 80 };
        save.Click += (_, _) => Submit(fill: false);
        var fill = new Button { Content = "Save and fill Keka", IsEnabled = canFill };
        if (!canFill)
        {
            ToolTipService.SetToolTip(fill, "Set the Keka address, project and work item in Settings first");
        }

        fill.Click += (_, _) => Submit(fill: true);
        var later = new Button { Content = "Not now" };
        later.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save);
        buttons.Children.Add(fill);
        buttons.Children.Add(later);

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = $"What did you do on {date.ToString("dddd, d MMMM", CultureInfo.CurrentCulture)}?",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_comment);
        panel.Children.Add(_hours);
        panel.Children.Add(_problem);
        panel.Children.Add(buttons);

        Content = new ScrollViewer
        {
            Content = panel,
            RequestedTheme = ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1B, 0x1B, 0x22)),
        };

        const int width = 520;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 520));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(work.X + Math.Max(0, (work.Width - width) / 2), work.Y + 80)); // under the notch
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true; // so it is seen even when another app is in front
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

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
