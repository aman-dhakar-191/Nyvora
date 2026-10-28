using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nyvora.App.Services;
using Nyvora.Core.Tasks;
using Windows.System;
using Windows.UI.Text;
using Windows.UI;

namespace Nyvora.App.UI;

public sealed partial class TasksPanel : UserControl
{
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE7, 0xE7, 0xEE));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x9A, 0x9A, 0xB0));
    private static readonly Brush DueBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));

    private TaskList _tasks = new();
    private UpdateService? _updates;

    public TasksPanel() => InitializeComponent();

    public event Action? CollapseRequested;

    public event Action? QuitRequested;

    internal void Attach(TaskList tasks, UpdateService updates)
    {
        _tasks.Changed -= Refresh;
        _tasks = tasks;
        _tasks.Changed += Refresh;
        Refresh();

        if (_updates is not null)
        {
            _updates.Changed -= RefreshUpdate;
        }

        _updates = updates;
        _updates.Changed += RefreshUpdate;
        RefreshUpdate();
    }

    private void RefreshUpdate()
    {
        if (_updates is not { } u)
        {
            return;
        }

        UpdateText.Text = u.State == UpdateState.Idle ? u.CurrentText : $"{u.CurrentText} · {u.Message}";
        var busy = u.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
        var installable = u.State == UpdateState.Available && u.Available is not null;

        UpdateButton.IsEnabled = !busy && (!installable || u.CanInstall);
        UpdateButton.Content = installable
            ? (u.CanInstall ? "Install and restart" : "Update available")
            : "Check for updates";
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updates is not { } u)
        {
            return;
        }

        try
        {
            if (u.State == UpdateState.Available)
            {
                await u.InstallAsync();
            }
            else
            {
                await u.CheckAsync();
            }
        }
        catch (Exception ex)
        {
            // Last resort so an unexpected failure cannot crash the app from an async void handler.
            Log.Error("Update action failed", ex);
        }
    }

    public void FocusInput() => Input.Focus(FocusState.Programmatic);

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var due = _tasks.DueCount(now);
        Summary.Text = due > 0 ? $"{_tasks.OpenCount} open · {due} due" : $"{_tasks.OpenCount} open";

        Rows.Children.Clear();
        foreach (var item in _tasks.Ordered())
        {
            Rows.Children.Add(BuildRow(item, now));
        }

        if (_tasks.Items.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = "Nothing yet. Add your first task above.",
                Foreground = MutedBrush,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
    }

    private UIElement BuildRow(TaskItem item, DateTimeOffset now)
    {
        var isDue = TaskList.IsDue(item, now);
        var row = new Grid { ColumnSpacing = 6, MinHeight = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new HandCheckBox { IsChecked = item.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => _tasks.Toggle(item.Id);
        row.Children.Add(check);

        var title = new TextBlock
        {
            Text = item.Title,
            Foreground = item.Done ? MutedBrush : TextBrush,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : TextDecorations.None,
        };
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        if (item.RemindAt is { } at && !item.Done)
        {
            var when = new TextBlock
            {
                Text = (isDue ? "Due " : "⏰ ") + Reminders.FormatDue(at, now),
                Foreground = isDue ? DueBrush : MutedBrush,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
        }

        if (isDue)
        {
            var snooze = new HandButton { Content = "Snooze", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
            snooze.Click += (_, _) => _tasks.Snooze(item.Id, DateTimeOffset.Now, Reminders.SnoozeDuration);
            Grid.SetColumn(snooze, 3);
            row.Children.Add(snooze);
        }

        var delete = new HandButton
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(delete, "Delete task");
        delete.Click += (_, _) => _tasks.Remove(item.Id);
        Grid.SetColumn(delete, 4);
        row.Children.Add(delete);

        return row;
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddFromInput(ReminderPreset.None);
            e.Handled = true;
        }
    }

    private void OnAddWithReminder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ReminderPreset>(tag, out var preset))
        {
            AddFromInput(preset);
        }
    }

    private void AddFromInput(ReminderPreset preset)
    {
        var now = DateTimeOffset.Now;
        if (_tasks.Add(Input.Text, now, Reminders.Resolve(preset, now)) is not null)
        {
            Input.Text = "";
        }

        Input.Focus(FocusState.Programmatic);
    }

    private void OnCollapse(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke();

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
