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

    public TasksPanel()
    {
        InitializeComponent();
        ClipboardText.Attach(Input);
    }

    public event Action? CollapseRequested;

    public event Action? QuitRequested;

    public event Action<bool>? AlwaysOnTopChanged;

    public event Action<bool>? StatusReminderChanged;

    private void OnStatusReminderChanged(bool on) => StatusReminderChanged?.Invoke(on);

    private string? _nudge;

    /// <summary>Shows a banner above the task list (cleared by <see cref="ClearNudge"/> when the pill collapses).</summary>
    public void ShowNudge(string message)
    {
        _nudge = message;
        ShowTasks();
        Refresh();
    }

    public void ClearNudge()
    {
        if (_nudge is not null)
        {
            _nudge = null;
            Refresh();
        }
    }

    private void OnTopClick(object sender, RoutedEventArgs e) => AlwaysOnTopChanged?.Invoke(TopToggle.IsChecked == true);

    internal void Attach(TaskList tasks, UpdateService updates, bool alwaysOnTop, StatusSet statuses, bool statusReminder)
    {
        TopToggle.IsChecked = alwaysOnTop;
        Settings.Attach(statuses, statusReminder);
        statuses.Changed += Refresh;
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

    /// <summary>Returns to the task list (used when the pill collapses so it always reopens on tasks).</summary>
    public void ShowTasks()
    {
        Settings.Visibility = Visibility.Collapsed;
        TasksView.Visibility = Visibility.Visible;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        TasksView.Visibility = Visibility.Collapsed;
        Settings.Visibility = Visibility.Visible;
    }

    private void OnSettingsBack() => ShowTasks();

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
        if (_nudge is not null)
        {
            Rows.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFB, 0xBF, 0x24)),
                Child = new TextBlock { Text = _nudge, FontSize = 13, Foreground = DueBrush, TextWrapping = TextWrapping.Wrap },
            });
        }

        foreach (var item in _tasks.Ordered())
        {
            Rows.Children.Add(BuildRow(item, now));
        }

        if (_tasks.Items.Count == 0 && _nudge is null)
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new HandCheckBox { IsChecked = item.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => _tasks.Toggle(item.Id);
        row.Children.Add(check);

        var chip = BuildChip(item);
        Grid.SetColumn(chip, 1);
        row.Children.Add(chip);

        var title = new TextBlock
        {
            Text = item.Title,
            Foreground = item.Done ? MutedBrush : TextBrush,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : TextDecorations.None,
        };
        Grid.SetColumn(title, 2);
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
            Grid.SetColumn(when, 3);
            row.Children.Add(when);
        }

        if (isDue)
        {
            var snooze = new HandButton { Content = "Snooze", Padding = new Thickness(8, 2, 8, 2), FontSize = 12 };
            snooze.Click += (_, _) => _tasks.Snooze(item.Id, DateTimeOffset.Now, Reminders.SnoozeDuration);
            Grid.SetColumn(snooze, 4);
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
        Grid.SetColumn(delete, 5);
        row.Children.Add(delete);

        return row;
    }

    // Done is green; open statuses take a colour by their position so neighbours are easy to tell apart.
    private static readonly Color[] OpenColors =
    {
        Color.FromArgb(0xFF, 0x9A, 0x9A, 0xB0),
        Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA),
        Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA),
        Color.FromArgb(0xFF, 0xF4, 0x72, 0xB6),
    };

    private static readonly Color DoneColor = Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80);

    private HandButton BuildChip(TaskItem item)
    {
        var status = _tasks.StatusOf(item);
        var color = status.IsDone ? DoneColor : OpenColors[Math.Max(0, _tasks.Statuses.IndexOf(status.Id)) % OpenColors.Length];
        var chip = new HandButton
        {
            Content = new TextBlock { Text = status.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 84 },
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 0,
            CornerRadius = new CornerRadius(10),
            Foreground = new SolidColorBrush(color),
            Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(chip, "Click to change status");
        chip.Click += (_, _) => _tasks.CycleStatus(item.Id);
        return chip;
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
