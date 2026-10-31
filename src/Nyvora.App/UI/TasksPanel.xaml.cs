using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
    private static readonly Brush TodayBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x5E, 0xEA, 0xD4));
    private static Color PriorityColor(TaskPriority p) => p switch
    {
        TaskPriority.High => Color.FromArgb(0xFF, 0xF8, 0x71, 0x71),
        TaskPriority.Medium => Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24),
        _ => Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA),
    };

    private static readonly Brush DueBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));

    private TaskList _tasks = new();
    private UpdateService? _updates;

    public TasksPanel()
    {
        InitializeComponent();
        ClipboardText.Attach(Input);
        ClipboardText.Attach(SearchBox);
    }

    /// <summary>The add box got "/notch text" (set the notch message) or a bare "/notch" (clear it).</summary>
    public event Action<string>? NotchRequested;

    public event Action? CollapseRequested;

    public event Action? QuitRequested;

    /// <summary>The gear (or the update notice) asks the window to toggle the settings panel.</summary>
    public event Action? SettingsRequested;

    /// <summary>The focus card was dismissed (nothing left to show) or the user asked for the full list.</summary>
    public event Action? FocusEnded;

    public event Action? FocusOpenAllRequested;

    private readonly List<Guid> _focusQueue = new();

    public bool IsFocusing => _focusQueue.Count > 0;

    /// <summary>Shows only these tasks, one at a time, as a card instead of the list.</summary>
    public void ShowFocus(IEnumerable<Guid> ids)
    {
        _focusQueue.Clear();
        _focusQueue.AddRange(ids);
        RenderFocus();
    }

    public void ClearFocus()
    {
        _focusQueue.Clear();
        FocusView.Visibility = Visibility.Collapsed;
        TasksView.Visibility = Visibility.Visible;
    }

    private TaskItem? FocusedItem() =>
        _focusQueue.Count == 0 ? null : _tasks.Items.FirstOrDefault(i => i.Id == _focusQueue[0]);

    private void RenderFocus()
    {
        // Skip tasks that were finished, deleted or snoozed since the card was queued.
        var now = DateTimeOffset.Now;
        while (_focusQueue.Count > 0 && !(FocusedItem() is { } t && TaskList.IsDue(t, now)))
        {
            _focusQueue.RemoveAt(0);
        }

        if (FocusedItem() is not { } item)
        {
            ClearFocus();
            FocusEnded?.Invoke();
            return;
        }

        TasksView.Visibility = Visibility.Collapsed;
        FocusView.Visibility = Visibility.Visible;
        FocusKicker.Text = item.RemindAt is { } at ? "Reminder · " + Reminders.FormatDue(at, now) : "Reminder";
        FocusMore.Text = _focusQueue.Count > 1 ? $"+{_focusQueue.Count - 1} more" : "";
        FocusTitle.Text = item.Title;
        FocusTags.Children.Clear();
        foreach (var tag in _tasks.TagsOf(item).Take(4))
        {
            FocusTags.Children.Add(TagVisuals.Chip(tag));
        }
    }

    private void OnFocusDone(object sender, RoutedEventArgs e)
    {
        if (FocusedItem() is { } item)
        {
            _tasks.Toggle(item.Id); // Changed -> Refresh -> RenderFocus moves on
        }
    }

    private void OnFocusSnooze(object sender, RoutedEventArgs e)
    {
        if (FocusedItem() is { } item)
        {
            _tasks.Snooze(item.Id, DateTimeOffset.Now, Reminders.SnoozeDuration);
        }
    }

    private void OnFocusOpenAll(object sender, RoutedEventArgs e)
    {
        ClearFocus();
        FocusOpenAllRequested?.Invoke();
    }

    private string? _nudge;
    private string _query = "";

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        if (SearchBar.Visibility == Visibility.Visible)
        {
            CloseSearch();
        }
        else
        {
            OpenSearch();
        }
    }

    private void OnSearchClose(object sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenSearch();
    }

    private void OpenSearch()
    {
        if (IsFocusing)
        {
            return; // the reminder card shows one task; there is no list to search
        }

        ReminderPicker.Visibility = Visibility.Collapsed;
        SwapInputAndSearch(search: true); // adding while searching is confusing: the new task may not match
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    private bool _searchOpen;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _swapTimer;

    /// <summary>The add box and the search box share one row: one fades out as the other fades in, so the list never jumps.</summary>
    private void SwapInputAndSearch(bool search)
    {
        _searchOpen = search;
        if (!search && _nudge is not null)
        {
            // A status reminder is showing and it has no add box, so closing the search must not bring one back.
            SearchBar.Visibility = Visibility.Collapsed;
            SearchBar.Opacity = 0;
            return;
        }

        var show = search ? (UIElement)SearchBar : InputRow;
        var hide = search ? (UIElement)InputRow : SearchBar;
        if (_swapTimer is null)
        {
            var fade = TimeSpan.FromMilliseconds(150);
            InputRow.OpacityTransition = new ScalarTransition { Duration = fade };
            SearchBar.OpacityTransition = new ScalarTransition { Duration = fade };
            _swapTimer = DispatcherQueue.CreateTimer();
            _swapTimer.Interval = TimeSpan.FromMilliseconds(170);
            _swapTimer.IsRepeating = false;
            _swapTimer.Tick += (_, _) => (_searchOpen ? (UIElement)InputRow : SearchBar).Visibility = Visibility.Collapsed;
        }

        show.Visibility = Visibility.Visible;
        show.IsHitTestVisible = true;
        hide.IsHitTestVisible = false;
        // Set after the element is laid out, so the opacity change is animated rather than applied at once.
        DispatcherQueue.TryEnqueue(() =>
        {
            show.Opacity = 1;
            hide.Opacity = 0;
        });
        _swapTimer.Stop();
        _swapTimer.Start();
    }

    private void CloseSearch()
    {
        SwapInputAndSearch(search: false);
        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = ""; // TextChanged clears the filter and redraws
        }
        else if (_query.Length > 0)
        {
            _query = "";
            Refresh();
        }
    }

    /// <summary>Back to the plain list (the pill collapsed): a leftover filter would look like missing tasks.</summary>
    public void ResetView() => CloseSearch();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text.Trim();
        Refresh();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            // First Esc clears the text, the next closes the bar; either way it must not collapse the pill.
            if (SearchBox.Text.Length > 0)
            {
                SearchBox.Text = "";
            }
            else
            {
                CloseSearch();
                Input.Focus(FocusState.Programmatic);
            }

            e.Handled = true;
        }
    }

    /// <summary>Shows a banner above the task list (cleared by <see cref="ClearNudge"/> when the pill collapses).</summary>
    public void ShowNudge(string message)
    {
        _nudge = message;
        // The hourly status reminder is for updating statuses, not for adding tasks: no add box (or anything hanging off it).
        InputRow.Visibility = Visibility.Collapsed;
        ReminderPicker.Visibility = Visibility.Collapsed;
        CommandHints.Visibility = Visibility.Collapsed;
        Refresh();
    }

    public void ClearNudge()
    {
        if (_nudge is not null)
        {
            _nudge = null;
            if (!_searchOpen)
            {
                InputRow.Visibility = Visibility.Visible;
                InputRow.Opacity = 1;
                InputRow.IsHitTestVisible = true;
            }

            Refresh();
        }
    }

    internal void Attach(TaskList tasks, UpdateService updates, StatusSet statuses, TagSet tags)
    {
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

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    /// <summary>A quiet hint in the footer when an update is waiting; installing lives in Settings.</summary>
    private void RefreshUpdateNotice()
    {
        var available = _updates is { State: UpdateState.Available, Available: { } info } ? info : null;
        UpdateNotice.Visibility = available is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotice.Content = available is null ? "" : $"Update {available.Tag} available";
    }

    public void FocusInput() => Input.Focus(FocusState.Programmatic);

    /// <summary>Ctrl+N: leave search (which hides the input) and put the cursor in the add box.</summary>
    public void ShowAddInput()
    {
        ClearNudge(); // asking to add a task is an explicit wish for the add box, so it comes back
        CloseSearch();
        FocusInput();
    }

    private readonly Stack<(TaskItem Item, int Index)> _deleted = new();

    /// <summary>Ctrl+Z: brings back the most recently deleted task, in its old place.</summary>
    public bool Undo()
    {
        while (_deleted.TryPop(out var last))
        {
            if (_tasks.Restore(last.Item, last.Index))
            {
                return true;
            }
        }

        return false;
    }

    private void Refresh()
    {
        if (_focusQueue.Count > 0)
        {
            RenderFocus();
        }

        var now = DateTimeOffset.Now;
        var due = _tasks.DueCount(now);
        var later = _tasks.LaterTodayCount(now);
        Summary.Text = $"{_tasks.OpenCount} open"
            + (due > 0 ? $" · {due} due" : "")
            + (later > 0 ? $" · {later} later today" : "");

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

        var filtering = TaskSearch.IsActive(_query);
        IReadOnlyList<TaskItem> ordered = _tasks.Ordered();
        var total = ordered.Count;
        if (filtering)
        {
            ordered = ordered.Where(t => _tasks.Matches(t, _query)).ToList();
            Summary.Text = $"{ordered.Count} of {total} tasks match";
        }

        var open = ordered.Where(t => !t.Done).ToList();
        var done = ordered.Where(t => t.Done).ToList();

        foreach (var item in open)
        {
            Rows.Children.Add(BuildRow(item, now));
        }

        if (open.Count == 0 && _nudge is null)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = filtering && done.Count == 0 ? $"No tasks match \u201c{_query}\u201d."
                    : done.Count == 0 ? "Nothing here yet. Add your first task above." : "All clear. Nice work.",
                Foreground = MutedBrush,
                FontSize = 13,
                Margin = new Thickness(2, 10, 0, 6),
            });
        }

        if (done.Count > 0)
        {
            // Finished tasks stay out of the way until asked for.
            var toggle = new HandButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = _showDone || filtering ? "\uE70E" : "\uE70D", FontSize = 10, Foreground = MutedBrush },
                        new TextBlock { Text = $"Done · {done.Count}", FontSize = 12, Foreground = MutedBrush },
                    },
                },
                Padding = new Thickness(6, 3, 8, 3),
                MinHeight = 0,
                Margin = new Thickness(0, 6, 0, 2),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            toggle.Click += (_, _) =>
            {
                _showDone = !_showDone;
                Refresh();
            };
            Rows.Children.Add(toggle);

            if (_showDone || filtering) // while searching, finished tasks that match are shown too
            {
                foreach (var item in done)
                {
                    Rows.Children.Add(BuildRow(item, now));
                }
            }
        }
    }

    private bool _showDone;

    private bool _compact;
    private const string HoverBarTag = "hover-actions";

    /// <summary>Compact layout: a status dot instead of the name, and the secondary controls on a line under the title.</summary>
    public void SetCompact(bool on)
    {
        if (_compact == on)
        {
            return;
        }

        _compact = on;
        Refresh();
    }

    private UIElement BuildRow(TaskItem item, DateTimeOffset now)
    {
        var isDue = TaskList.IsDue(item, now);
        // Secondary actions stay invisible (but keep their space, so nothing shifts) until the row is hovered or focused.
        var reveal = new List<UIElement>();
        var row = new Grid { ColumnSpacing = 6 };
        // Compact is one line per task. What matters stays on the line (a status dot, small tag dots, the due time);
        // everything else is in a bar that appears over the right end of the row while it is hovered.
        var trail = _compact ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center } : null;
        var hoverBar = _compact
            ? new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x17, 0x17, 0x1C)), // the row's hover color, opaque, so the title does not show through
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 0, 0, 0),
                Opacity = 0,
                IsHitTestVisible = false,
                Tag = HoverBarTag,
                OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) },
            }
            : null;
        void Place(FrameworkElement el, int column, bool inline = false)
        {
            if (trail is not null)
            {
                (inline ? trail : hoverBar!).Children.Add(el);
                return;
            }

            Grid.SetColumn(el, column);
            row.Children.Add(el);
        }

        row.RowDefinitions.Add(new RowDefinition { MinHeight = _compact ? 30 : 38 });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tags, aligned under the title
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_compact ? 18 : 90) }); // fixed, so every title starts at the same x
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (_compact ? 1 : 4); i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        if (_compact)
        {
            row.Padding = new Thickness(7, 0, 0, 0); // room for the priority bar on the left edge
        }

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
            IsTextSelectionEnabled = true, // drag to select, Ctrl+C to copy
        };
        if (item.Priority != TaskPriority.None && !item.Done && !_compact)
        {
            // A colored dot in front of the title carries the priority at a glance.
            title.Text = "";
            title.Inlines.Add(new Run { Text = "\u25CF ", Foreground = new SolidColorBrush(PriorityColor(item.Priority)) });
            title.Inlines.Add(new Run { Text = item.Title });
        }

        if (item.Priority == TaskPriority.High && !item.Done)
        {
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        if (item.Done)
        {
            row.Opacity = 0.7; // finished work recedes
        }

        ToolTipService.SetToolTip(title, "Double-click to edit");
        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            BeginTitleEdit(row, title, item);
        };
        // A selectable TextBlock consumes its own double-click (it selects a word), so DoubleTapped alone never
        // arrives. Watch raw presses, including handled ones, and spot the second click ourselves.
        var lastPress = DateTime.MinValue;
        title.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var now = DateTime.UtcNow;
            if ((now - lastPress).TotalMilliseconds < 450)
            {
                lastPress = DateTime.MinValue;
                BeginTitleEdit(row, title, item);
            }
            else
            {
                lastPress = now;
            }
        }), true);
        Grid.SetColumn(title, 2);
        row.Children.Add(title);

        // Hover shows the whole title, wrapped; leaving restores the trimmed single line.
        row.Background = new SolidColorBrush(Colors.Transparent); // so the empty parts of the row get pointer events too
        row.CornerRadius = new CornerRadius(6);
        // Fade the highlight in and out instead of snapping.
        row.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(140) };

        // A title that is cut off grows to show everything, but only once the pointer rests on the row. Sweeping
        // the mouse down the list never makes rows jump, and a title that already fits never changes.
        var expandTimer = DispatcherQueue.CreateTimer();
        expandTimer.Interval = TimeSpan.FromMilliseconds(380);
        expandTimer.IsRepeating = false;
        expandTimer.Tick += (_, _) =>
        {
            if (title.IsTextTrimmed)
            {
                title.TextTrimming = TextTrimming.None;
                title.TextWrapping = TextWrapping.Wrap;
                if (hoverBar is not null && trail is not null)
                {
                    // Compact: the hover bar sits over the right end of the row, so wrap the title before it
                    // instead of letting the bar cover the last letters. The trail (due time) is under the bar too.
                    hoverBar.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    var covered = hoverBar.DesiredSize.Width - trail.ActualWidth - row.ColumnSpacing;
                    title.Margin = new Thickness(0, 0, Math.Max(0, covered) + 4, 0);
                }
            }
        };
        row.PointerEntered += (_, _) =>
        {
            row.Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            foreach (var el in reveal)
            {
                el.Opacity = 1;
            }

            if (hoverBar is not null)
            {
                hoverBar.Opacity = 1;
                hoverBar.IsHitTestVisible = true;
            }

            expandTimer.Start();
        };
        row.PointerExited += (_, _) =>
        {
            expandTimer.Stop();
            title.Margin = new Thickness(0);
            title.TextWrapping = TextWrapping.NoWrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Background = new SolidColorBrush(Colors.Transparent);
            foreach (var el in reveal)
            {
                el.Opacity = 0;
            }

            if (hoverBar is not null)
            {
                hoverBar.Opacity = 0;
                hoverBar.IsHitTestVisible = false;
            }
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
            Place(tagButton, 3);
            if (itemTags.Count == 0 && _editingTags != item.Id)
            {
                reveal.Add(tagButton);
            }
        }

        if (!item.Done)
        {
            // Always present on open tasks: shows the reminder, or "Add", and opens the inline editor.
            var when = new HandButton
            {
                Content = new TextBlock
                {
                    Text = item.RemindAt is { } at
                        ? (_compact ? "" : isDue ? "Due " : "⏰ ") + Reminders.FormatDue(at, now) // compact: the time alone, the amber pill says "due"
                        : "⏰",
                    Opacity = item.RemindAt is null ? 0.55 : 1, // a quiet bell until a reminder exists
                    FontSize = 12,
                    Foreground = isDue ? DueBrush : TaskList.IsLaterToday(item, now) ? TodayBrush : MutedBrush,
                },
                Padding = new Thickness(isDue ? 9 : 6, 2, isDue ? 9 : 6, 2),
                MinHeight = 0,
                CornerRadius = new CornerRadius(10),
                // An overdue reminder is a small amber pill, so it stands out from the quiet grey ones.
                Background = new SolidColorBrush(isDue ? Color.FromArgb(0x26, 0xFB, 0xBF, 0x24) : Colors.Transparent),
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
            Place(when, 4, inline: item.RemindAt is not null);
            if (item.RemindAt is null && _editingReminder != item.Id)
            {
                reveal.Add(when);
            }
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
            Place(snooze, 5);
        }

        var delete = new HandButton
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(delete, "Delete task");
        delete.Click += (_, _) =>
        {
            var index = _tasks.IndexOf(item.Id);
            if (_tasks.Remove(item.Id))
            {
                _deleted.Push((item, index));
            }
        };

        // Double-click edits too, but a visible pencil is the dependable way in.
        var edit = new HandButton
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(edit, "Edit title");
        edit.Click += (_, _) => BeginTitleEdit(row, title, item);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (!item.Done)
        {
            // One click steps through none, high, medium, low. The flag is colored once a priority is set.
            var prio = new HandButton
            {
                Content = new FontIcon
                {
                    Glyph = "\uE7C1",
                    FontSize = 12,
                    Foreground = item.Priority == TaskPriority.None ? MutedBrush : new SolidColorBrush(PriorityColor(item.Priority)),
                },
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(prio, $"Priority: {TaskPriorities.Name(item.Priority)}. Click for {TaskPriorities.Name(TaskPriorities.Next(item.Priority))}");
            prio.Click += (_, _) => _tasks.CyclePriority(item.Id);
            actions.Children.Add(prio);
            reveal.Add(prio);

            if (!_compact)
            {
                // Compact rows leave the pencil out for space; double-clicking the title still edits.
                actions.Children.Add(edit);
                reveal.Add(edit);
            }
        }

        actions.Children.Add(delete);
        Place(actions, 6);
        reveal.Add(delete);

        foreach (var el in reveal)
        {
            el.Opacity = 0;
            el.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) }; // after the first 0: no flash on redraw
            el.GotFocus += (_, _) => el.Opacity = 1; // keyboard users still find them
        }

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(row);

        if (trail is not null)
        {
            // Small tag dots, then the due time: the always-visible part of a compact row.
            for (var i = Math.Min(itemTags.Count, 3) - 1; i >= 0; i--)
            {
                var dot = new Border
                {
                    Width = 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(3.5),
                    Background = new SolidColorBrush(TagVisuals.Of(itemTags[i].Color)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(dot, itemTags[i].Name);
                trail.Children.Insert(0, dot);
            }

            Grid.SetColumn(trail, 3);
            row.Children.Add(trail);

            // The hover bar sits over the title's right end and the trail; it is on top, so it is added last.
            Grid.SetColumn(hoverBar!, 2);
            Grid.SetColumnSpan(hoverBar!, 2);
            row.Children.Add(hoverBar!);
            hoverBar!.GotFocus += (_, _) =>
            {
                hoverBar.Opacity = 1;
                hoverBar.IsHitTestVisible = true;
            };

            if (item.Priority != TaskPriority.None && !item.Done)
            {
                var bar = new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(PriorityColor(item.Priority)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(-7, 6, 0, 6), // hangs into the row's left padding
                };
                ToolTipService.SetToolTip(bar, $"Priority: {TaskPriorities.Name(item.Priority)}");
                Grid.SetColumnSpan(bar, 4);
                row.Children.Add(bar);
            }
        }

        if (!_compact && itemTags.Count > 0)
        {
            // Under the title (same grid, so it lines up exactly); long lists collapse into "+N".
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var tag in itemTags.Take(4))
            {
                line.Children.Add(TagVisuals.Chip(tag));
            }

            if (itemTags.Count > 4)
            {
                line.Children.Add(new TextBlock { Text = $"+{itemTags.Count - 4}", FontSize = 11, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center });
            }

            Grid.SetRow(line, 1);
            Grid.SetColumn(line, 2);
            Grid.SetColumnSpan(line, 5);
            row.Children.Add(line);
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

    /// <summary>Swaps the title for a text box: Enter or clicking away saves, Esc cancels.</summary>
    private void BeginTitleEdit(Grid row, TextBlock title, TaskItem item)
    {
        if (!row.Children.Contains(title))
        {
            return; // already editing (double-click can arrive through two paths)
        }

        // Wraps, so a long title is fully visible while it is edited; Enter saves, Esc cancels, clicking away saves.
        var box = new TextBox
        {
            Text = item.Title,
            MaxLength = TaskList.MaxTitleLength,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            MinHeight = 34,
            Padding = new Thickness(10, 6, 10, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ClipboardText.Attach(box);
        var hint = new TextBlock { Text = "Enter to save · Esc to cancel", FontSize = 11, Foreground = MutedBrush };
        var editor = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        editor.Children.Add(box);
        editor.Children.Add(hint);

        row.Children.Remove(title);
        // The editor takes the title's place plus the room of the row's buttons, which are hidden while editing.
        foreach (var el in row.Children.OfType<FrameworkElement>().ToList())
        {
            if (Grid.GetRow(el) == 0 && (Grid.GetColumn(el) >= 3 || Equals(el.Tag, HoverBarTag)))
            {
                el.Visibility = Visibility.Collapsed;
            }
        }

        Grid.SetColumn(editor, 2);
        Grid.SetColumnSpan(editor, 5);
        row.Children.Add(editor);

        var finished = false;
        void Finish(bool save)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            if (!save || !_tasks.Rename(item.Id, box.Text))
            {
                Refresh(); // cancelled, blank or unchanged: just draw the row again
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Finish(true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                Finish(false);
                e.Handled = true; // Esc cancels the edit instead of collapsing the pill
            }
        };
        box.LostFocus += (_, _) => Finish(true);
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
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

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = BuildReminderPicker(item.RemindAt, Apply, () =>
            {
                _editingReminder = null;
                Refresh();
            }),
        };
    }

    /// <summary>
    /// The one reminder picker (beside the input and on a task): quick choices, a box that understands plain words
    /// ("in 2h", "tomorrow 9am", "fri 5pm", "nov 2") with a live preview, and the time picker one click away.
    /// <paramref name="pick"/> gets the chosen time, or null for Clear; <paramref name="close"/> is for Esc.
    /// </summary>
    private UIElement BuildReminderPicker(DateTimeOffset? current, Action<DateTimeOffset?> pick, Action close)
    {
        var now = DateTimeOffset.Now;

        HandButton Chip(string label, Func<DateTimeOffset?> at, bool enabled = true)
        {
            var b = new HandButton { Content = label, FontSize = 12, Padding = new Thickness(8, 2, 8, 2), MinHeight = 0, IsEnabled = enabled };
            b.Click += (_, _) => pick(at());
            return b;
        }

        DateTimeOffset? Parsed(string text)
        {
            var n = DateTimeOffset.Now;
            return ReminderText.TryParse(text, n, out var at) ? at : null;
        }

        var quick1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        quick1.Children.Add(Chip("In 10 min", () => DateTimeOffset.Now.AddMinutes(10)));
        quick1.Children.Add(Chip("In 1 hour", () => DateTimeOffset.Now.AddHours(1)));
        if (now.Hour < 17)
        {
            quick1.Children.Add(Chip("This evening", () => Parsed("today 18:00")));
        }

        var quick2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        quick2.Children.Add(Chip("Tomorrow 9:00", () => Parsed("tomorrow 9am")));
        quick2.Children.Add(Chip("Next Mon 9:00", () => Parsed("next monday 9am")));
        quick2.Children.Add(Chip("Clear", () => null, enabled: current is not null));

        DateTimeOffset? typed = null;
        var input = new TextBox
        {
            PlaceholderText = "Or type: in 2h, tomorrow 9am, fri 5pm, nov 2",
            MaxLength = 40,
            MinHeight = 32,
            Padding = new Thickness(10, 6, 10, 6),
        };
        ClipboardText.Attach(input);
        var preview = new TextBlock { FontSize = 12, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap };
        var set = new HandButton { Content = "Set", IsEnabled = false, Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Top };

        input.TextChanged += (_, _) =>
        {
            var n = DateTimeOffset.Now;
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                typed = null;
                preview.Text = current is { } c ? "Now: " + Reminders.FormatDue(c, n) : "";
                preview.Foreground = MutedBrush;
            }
            else if (ReminderText.TryParse(input.Text, n, out var at))
            {
                typed = at;
                preview.Text = "→ " + Reminders.FormatDue(at, n);
                preview.Foreground = TodayBrush;
            }
            else
            {
                typed = null;
                preview.Text = "Not understood yet. Try \u201cin 2h\u201d, \u201ctomorrow 9am\u201d or \u201cfri 17:00\u201d.";
                preview.Foreground = MutedBrush;
            }

            set.IsEnabled = typed is not null;
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (typed is { } at)
                {
                    pick(at);
                }

                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                close(); // closes the picker; it must not collapse the pill
                e.Handled = true;
            }
        };
        set.Click += (_, _) =>
        {
            if (typed is { } at)
            {
                pick(at);
            }
        };
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        if (current is { } cur)
        {
            preview.Text = "Now: " + Reminders.FormatDue(cur, now);
        }

        var typedRow = new Grid { ColumnSpacing = 8 };
        typedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        typedRow.Children.Add(input);
        Grid.SetColumn(set, 1);
        typedRow.Children.Add(set);

        // The time picker stays available, one click away, so the common case is not buried under it.
        var suggestion = now.AddMinutes(15 - now.Minute % 15);
        var picker = new TimePicker { Time = new TimeSpan(suggestion.Hour, suggestion.Minute, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var setTime = new HandButton { Content = "Set", Padding = new Thickness(14, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center };
        setTime.Click += (_, _) =>
        {
            var t = picker.Time;
            if (Reminders.TryParseClock($"{t.Hours}:{t.Minutes:00}", DateTimeOffset.Now, out var at))
            {
                pick(at);
            }
        };
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
        timeRow.Children.Add(picker);
        timeRow.Children.Add(setTime);
        var more = new HandButton
        {
            Content = "Pick a time\u2026",
            FontSize = 12,
            Padding = new Thickness(4, 2, 8, 2),
            MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        more.Click += (_, _) =>
        {
            more.Visibility = Visibility.Collapsed;
            timeRow.Visibility = Visibility.Visible;
        };

        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(quick1);
        box.Children.Add(quick2);
        box.Children.Add(typedRow);
        box.Children.Add(preview);
        box.Children.Add(more);
        box.Children.Add(timeRow);
        return box;
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
        if (_compact)
        {
            // A small colored dot; the name is in the tooltip and clicking still steps the status.
            var dot = new HandButton
            {
                // An 18 px target around a 10 px dot: easy to hit, still quiet.
                Width = 18,
                Height = 18,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Content = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color) },
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(dot, $"{status.Name}: click to change status");
            dot.Click += (_, _) => _tasks.CycleStatus(item.Id);
            return dot;
        }

        var chip = new HandButton
        {
            // Every chip fills the same fixed column, so the pills line up as a neat column and short names leave no gap.
            Content = new TextBlock { Text = status.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 0,
            CornerRadius = new CornerRadius(10),
            Foreground = new SolidColorBrush(color),
            Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(chip, $"{status.Name}: click to change status");
        chip.Click += (_, _) => _tasks.CycleStatus(item.Id);
        return chip;
    }

    private NotchSuggestionSet _hints = NotchSuggestionSet.None;

    /// <summary>Shows what a slash command can take next, as clickable chips under the add box.</summary>
    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        _hints = NotchSuggestions.For(Input.Text);
        CommandChips.Children.Clear();
        foreach (var s in _hints.Items)
        {
            var chip = new HandButton
            {
                Content = new TextBlock { Text = s.Insert, FontSize = 12 },
                Padding = new Thickness(9, 2, 9, 2),
                MinHeight = 0,
                CornerRadius = new CornerRadius(12),
            };
            ToolTipService.SetToolTip(chip, s.Hint);
            var picked = s;
            chip.Click += (_, _) => ApplyHint(picked);
            CommandChips.Children.Add(chip);
        }

        CommandHintText.Text = _hints.Hint ?? "";
        CommandHintText.Visibility = string.IsNullOrEmpty(_hints.Hint) ? Visibility.Collapsed : Visibility.Visible;
        CommandHints.Visibility = _hints.Items.Count > 0 || !string.IsNullOrEmpty(_hints.Hint) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyHint(NotchSuggestion s)
    {
        Input.Text = _hints.Apply(s);
        Input.SelectionStart = Input.Text.Length;
        Input.Focus(FocusState.Programmatic);
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab && _hints.Items.Count > 0)
        {
            ApplyHint(_hints.Items[0]); // Tab takes the first suggestion
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            AddFromInput();
            e.Handled = true;
        }
    }

    // The reminder chosen next to the input applies to the next task added, then resets.
    private ReminderPreset _pendingPreset = ReminderPreset.None;
    private DateTimeOffset? _pendingAt;

    private void OnReminderButtonClick(object sender, RoutedEventArgs e)
    {
        if (ReminderPicker.Visibility == Visibility.Visible)
        {
            ReminderPicker.Visibility = Visibility.Collapsed;
            return;
        }

        ReminderPicker.Child = BuildReminderPicker(
            _pendingAt,
            at => Arm(ReminderPreset.None, at),
            () =>
            {
                ReminderPicker.Visibility = Visibility.Collapsed;
                Input.Focus(FocusState.Programmatic);
            });
        ReminderPicker.Visibility = Visibility.Visible;
    }

    private void Arm(ReminderPreset preset, DateTimeOffset? at)
    {
        _pendingPreset = preset;
        _pendingAt = at;
        ReminderPicker.Visibility = Visibility.Collapsed;
        UpdateReminderButton();
        Input.Focus(FocusState.Programmatic);
    }

    private void UpdateReminderButton()
    {
        var armed = _pendingPreset != ReminderPreset.None || _pendingAt is not null;
        ReminderLabel.Text = _pendingAt is { } at
            ? "⏰ " + Reminders.FormatDue(at, DateTimeOffset.Now)
            : _pendingPreset switch
            {
                ReminderPreset.InTenMinutes => "⏰ 10 min",
                ReminderPreset.InOneHour => "⏰ 1 hour",
                ReminderPreset.TomorrowMorning => "⏰ Tomorrow",
                _ => "⏰",
            };
        ReminderLabel.Foreground = armed ? DueBrush : TextBrush;
        ToolTipService.SetToolTip(ReminderButton, armed ? "Reminder for the next task (click to change)" : "Add a reminder to the next task");
    }

    private void AddFromInput()
    {
        if (NotchMessage.TryParseCommand(Input.Text, out var notch))
        {
            Input.Text = "";
            NotchRequested?.Invoke(notch);
            Input.Focus(FocusState.Programmatic);
            return;
        }

        var now = DateTimeOffset.Now;
        var remindAt = _pendingAt ?? Reminders.Resolve(_pendingPreset, now);
        if (_tasks.Add(Input.Text, now, remindAt) is not null)
        {
            Input.Text = "";
            _pendingPreset = ReminderPreset.None;
            _pendingAt = null;
            UpdateReminderButton();
        }

        Input.Focus(FocusState.Programmatic);
    }

    private void OnCollapse(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke();

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
