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
        Settings.AlwaysOnTopChanged += on => AlwaysOnTopChanged?.Invoke(on);
        Settings.StatusReminderChanged += on => StatusReminderChanged?.Invoke(on);
    }

    public event Action? CollapseRequested;

    public event Action? QuitRequested;

    public event Action<bool>? AlwaysOnTopChanged;

    public event Action<bool>? StatusReminderChanged;

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

    internal void Attach(TaskList tasks, UpdateService updates, bool alwaysOnTop, StatusSet statuses, TagSet tags, bool statusReminder)
    {
        Settings.Attach(statuses, tags, updates, alwaysOnTop, statusReminder);
        tags.Changed += Refresh;
        statuses.Changed += Refresh;
        _tasks.Changed -= Refresh;
        _tasks = tasks;
        _tasks.Changed += Refresh;
        Refresh();

        if (_updates is not null)
        {
            _updates.Changed -= RefreshUpdateNotice;
        }

        _updates = updates;
        _updates.Changed += RefreshUpdateNotice;
        RefreshUpdateNotice();
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

    /// <summary>A quiet hint in the footer when an update is waiting; installing lives in Settings.</summary>
    private void RefreshUpdateNotice()
    {
        var available = _updates is { State: UpdateState.Available, Available: { } info } ? info : null;
        UpdateNotice.Visibility = available is null ? Visibility.Collapsed : Visibility.Visible;
        Hint.Visibility = available is null ? Visibility.Visible : Visibility.Collapsed;
        UpdateNotice.Content = available is null ? "" : $"Update {available.Tag} available";
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

        // Hover shows the whole title, wrapped; leaving restores the trimmed single line.
        row.Background = new SolidColorBrush(Colors.Transparent); // so the empty parts of the row get pointer events too
        row.CornerRadius = new CornerRadius(6);
        row.PointerEntered += (_, _) =>
        {
            title.TextTrimming = TextTrimming.None;
            title.TextWrapping = TextWrapping.Wrap;
            row.Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
        };
        row.PointerExited += (_, _) =>
        {
            title.TextWrapping = TextWrapping.NoWrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Background = new SolidColorBrush(Colors.Transparent);
        };

        var itemTags = _tasks.TagsOf(item);
        if (!item.Done)
        {
            // A quiet "#" until the task has tags; opens the inline tag picker.
            var tagButton = new HandButton
            {
                Content = new TextBlock
                {
                    Text = "#",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = itemTags.Count > 0 ? new SolidColorBrush(TagVisuals.Of(itemTags[0].Color)) : MutedBrush,
                    Opacity = itemTags.Count > 0 ? 1 : 0.55,
                },
                Padding = new Thickness(6, 2, 6, 2),
                MinHeight = 0,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(tagButton, "Tags");
            tagButton.Click += (_, _) =>
            {
                _editingTags = _editingTags == item.Id ? null : item.Id;
                _editingReminder = null;
                Refresh();
            };
            Grid.SetColumn(tagButton, 3);
            row.Children.Add(tagButton);
        }

        if (!item.Done)
        {
            // Always present on open tasks: shows the reminder, or "Add", and opens the inline editor.
            var when = new HandButton
            {
                Content = new TextBlock
                {
                    Text = item.RemindAt is { } at ? (isDue ? "Due " : "⏰ ") + Reminders.FormatDue(at, now) : "⏰",
                    Opacity = item.RemindAt is null ? 0.55 : 1, // a quiet bell until a reminder exists
                    FontSize = 12,
                    Foreground = isDue ? DueBrush : MutedBrush,
                },
                Padding = new Thickness(6, 2, 6, 2),
                MinHeight = 0,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(when, item.RemindAt is null ? "Add a reminder" : "Change or clear the reminder");
            when.Click += (_, _) =>
            {
                _editingReminder = _editingReminder == item.Id ? null : item.Id;
                _editingTags = null;
                Refresh();
            };
            Grid.SetColumn(when, 4);
            row.Children.Add(when);
        }

        if (isDue)
        {
            var snooze = new HandButton
            {
                Content = new TextBlock { Text = "💤", FontSize = 14 },
                Padding = new Thickness(6, 2, 6, 2),
                MinHeight = 0,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(snooze, $"Snooze {(int)Reminders.SnoozeDuration.TotalMinutes} minutes");
            snooze.Click += (_, _) => _tasks.Snooze(item.Id, DateTimeOffset.Now, Reminders.SnoozeDuration);
            Grid.SetColumn(snooze, 5);
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
        Grid.SetColumn(delete, 6);
        row.Children.Add(delete);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(row);

        if (itemTags.Count > 0)
        {
            // Under the title; long lists collapse into "+N" so the row never grows sideways.
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(30, 0, 0, 2) };
            foreach (var tag in itemTags.Take(4))
            {
                line.Children.Add(TagVisuals.Chip(tag));
            }

            if (itemTags.Count > 4)
            {
                line.Children.Add(new TextBlock { Text = $"+{itemTags.Count - 4}", FontSize = 11, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
            }

            stack.Children.Add(line);
        }

        if (!item.Done && _editingReminder == item.Id)
        {
            stack.Children.Add(BuildReminderEditor(item));
        }

        if (!item.Done && _editingTags == item.Id)
        {
            stack.Children.Add(BuildTagEditor(item));
        }

        return stack;
    }

    private Guid? _editingReminder;
    private Guid? _editingTags;

    private UIElement BuildTagEditor(TaskItem item)
    {
        var box = new StackPanel { Spacing = 6 };

        if (_tasks.Tags.Items.Count == 0)
        {
            box.Children.Add(new TextBlock { Text = "No tags yet. Create your first one below.", FontSize = 12, Foreground = MutedBrush });
        }
        else
        {
            var list = new StackPanel { Spacing = 2 };
            foreach (var tag in _tasks.Tags.Items)
            {
                var check = new HandCheckBox
                {
                    IsChecked = item.TagIds.Contains(tag.Id),
                    MinWidth = 0,
                    Content = new TextBlock { Text = "#" + tag.Name, FontSize = 13, Foreground = new SolidColorBrush(TagVisuals.Of(tag.Color)) },
                };
                check.Click += (_, _) => _tasks.ToggleTag(item.Id, tag.Id);
                list.Children.Add(check);
            }

            box.Children.Add(new ScrollViewer { Content = list, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var error = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0x71, 0x85)), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var name = new TextBox { PlaceholderText = "New tag", MaxLength = TagSet.MaxNameLength };
        ClipboardText.Attach(name);
        name.Loaded += (_, _) => name.StartBringIntoView(); // the list area is small; keep the editor visible

        void AddTag()
        {
            var created = _tasks.Tags.TryAdd(name.Text, out var message);
            if (created is null)
            {
                error.Text = message ?? "";
                error.Visibility = Visibility.Visible;
                return;
            }

            _tasks.ToggleTag(item.Id, created.Id); // a tag created from a task is also put on that task
        }

        name.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                AddTag();
                e.Handled = true;
            }
        };

        var add = new HandButton { Content = "Add", FontSize = 12, Padding = new Thickness(10, 4, 10, 4) };
        add.Click += (_, _) => AddTag();
        var done = new HandButton { Content = "Done", FontSize = 12, Padding = new Thickness(10, 4, 10, 4) };
        done.Click += (_, _) =>
        {
            _editingTags = null;
            Refresh();
        };

        var newRow = new Grid { ColumnSpacing = 6 };
        newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        newRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        newRow.Children.Add(name);
        Grid.SetColumn(add, 1);
        newRow.Children.Add(add);
        Grid.SetColumn(done, 2);
        newRow.Children.Add(done);
        box.Children.Add(newRow);
        box.Children.Add(error);

        return new Border
        {
            Margin = new Thickness(30, 0, 0, 6),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = box,
        };
    }

    private UIElement BuildReminderEditor(TaskItem item)
    {
        void Apply(DateTimeOffset? at)
        {
            _editingReminder = null;
            _tasks.SetReminder(item.Id, at);
        }

        HandButton Button(string label, Action onClick, bool enabled = true)
        {
            var b = new HandButton { Content = label, FontSize = 12, Padding = new Thickness(8, 2, 8, 2), MinHeight = 0, IsEnabled = enabled };
            b.Click += (_, _) => onClick();
            return b;
        }

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        presets.Children.Add(Button("10 min", () => Apply(Reminders.Resolve(ReminderPreset.InTenMinutes, DateTimeOffset.Now))));
        presets.Children.Add(Button("1 hour", () => Apply(Reminders.Resolve(ReminderPreset.InOneHour, DateTimeOffset.Now))));
        presets.Children.Add(Button("Tomorrow 9:00", () => Apply(Reminders.Resolve(ReminderPreset.TomorrowMorning, DateTimeOffset.Now))));
        presets.Children.Add(Button("Clear", () => Apply(null), enabled: item.RemindAt is not null));

        // A time that has already passed today means the same time tomorrow (see Reminders.TryParseClock).
        var now = DateTimeOffset.Now;
        var suggestion = now.AddMinutes(15 - now.Minute % 15);
        var picker = new TimePicker { Time = new TimeSpan(suggestion.Hour, suggestion.Minute, 0), HorizontalAlignment = HorizontalAlignment.Left };
        picker.Loaded += (_, _) => picker.StartBringIntoView(); // the list area is small; keep the editor visible
        void SetFromPicker()
        {
            var t = picker.Time;
            if (Reminders.TryParseClock($"{t.Hours}:{t.Minutes:00}", DateTimeOffset.Now, out var at))
            {
                Apply(at);
            }
        }

        var custom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        custom.Children.Add(picker);
        custom.Children.Add(new HandButton
        {
            Content = "Set",
            Padding = new Thickness(14, 5, 14, 5),
            VerticalAlignment = VerticalAlignment.Center,
        });
        ((HandButton)custom.Children[1]).Click += (_, _) => SetFromPicker();

        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(presets);
        box.Children.Add(custom);

        return new Border
        {
            Margin = new Thickness(30, 0, 0, 6),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = box,
        };
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
