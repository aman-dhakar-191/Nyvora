using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nyvora.App.Services;
using Nyvora.Core.Tasks;
using Windows.System;
using Windows.UI;

namespace Nyvora.App.UI;

/// <summary>Edits the user's task statuses. All validation lives in <see cref="StatusSet"/>; this only shows its messages.</summary>
public sealed partial class StatusSettingsView : UserControl
{
    private StatusSet _statuses = new();

    public StatusSettingsView()
    {
        InitializeComponent();
        ClipboardText.Attach(NewName);
    }

    public event Action? BackRequested;

    public event Action<bool>? StatusReminderChanged;

    private void OnReminderClick(object sender, RoutedEventArgs e) =>
        StatusReminderChanged?.Invoke(ReminderToggle.IsChecked == true);

    internal void Attach(StatusSet statuses, bool statusReminder)
    {
        ReminderToggle.IsChecked = statusReminder;
        _statuses.Changed -= Rebuild;
        _statuses = statuses;
        _statuses.Changed += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Children.Clear();
        var items = _statuses.Items;
        for (var i = 0; i < items.Count; i++)
        {
            Rows.Children.Add(BuildRow(items[i], i, items.Count));
        }
    }

    private UIElement BuildRow(StatusDef status, int index, int count)
    {
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var c = 0; c < 4; c++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var name = new TextBox { Text = status.Name, MaxLength = StatusSet.MaxNameLength };
        ClipboardText.Attach(name);
        void Commit()
        {
            if (name.Text.Trim() == status.Name)
            {
                return;
            }

            if (Show(_statuses.Rename(status.Id, name.Text)))
            {
                name.Text = status.Name; // rejected: put the old name back
            }
        }

        name.LostFocus += (_, _) => Commit();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        row.Children.Add(name);

        var done = new HandCheckBox { Content = "Done", IsChecked = status.IsDone, MinWidth = 0, Margin = new Thickness(6, 0, 0, 0) };
        done.Click += (_, _) =>
        {
            if (Show(_statuses.SetDone(status.Id, done.IsChecked == true)))
            {
                done.IsChecked = status.IsDone;
            }
        };
        Place(row, done, 1);

        Place(row, IconButton("", "Move up", index > 0, () => _statuses.Move(status.Id, -1)), 2);
        Place(row, IconButton("", "Move down", index < count - 1, () => _statuses.Move(status.Id, 1)), 3);
        Place(row, IconButton("", "Remove status", true, () => _statuses.Remove(status.Id)), 4);
        return row;
    }

    private static void Place(Grid row, UIElement element, int column)
    {
        Grid.SetColumn((FrameworkElement)element, column);
        row.Children.Add(element);
    }

    private HandButton IconButton(string glyph, string tip, bool enabled, Func<string?> action)
    {
        var b = new HandButton
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Padding = new Thickness(7),
            IsEnabled = enabled,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => Show(action());
        return b;
    }

    /// <summary>Shows an error (or clears it) and returns true when there was one.</summary>
    private bool Show(string? error)
    {
        ErrorText.Text = error ?? "";
        return error is not null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (!Show(_statuses.Add(NewName.Text, NewDone.IsChecked == true)))
        {
            NewName.Text = "";
            NewDone.IsChecked = false;
        }
    }

    private void OnNewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            OnAdd(sender, e);
            e.Handled = true;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        BackRequested?.Invoke();
    }
}
