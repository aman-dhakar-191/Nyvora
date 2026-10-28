using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nyvora.App.Services;
using Nyvora.Core.Tasks;
using Windows.System;
using Windows.UI;

namespace Nyvora.App.UI;

/// <summary>Creates, renames, recolors and deletes tags. Validation lives in <see cref="TagSet"/>.</summary>
public sealed partial class TagSettingsView : UserControl
{
    private TagSet _tags = new();

    public TagSettingsView()
    {
        InitializeComponent();
        ClipboardText.Attach(NewName);
    }

    internal void Attach(TagSet tags)
    {
        _tags.Changed -= Rebuild;
        _tags = tags;
        _tags.Changed += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Children.Clear();
        foreach (var tag in _tags.Items)
        {
            Rows.Children.Add(BuildRow(tag));
        }
    }

    private UIElement BuildRow(TagDef tag)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var color = TagVisuals.Of(tag.Color);
        var swatch = new HandButton
        {
            Width = 28,
            Height = 28,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(color) },
        };
        ToolTipService.SetToolTip(swatch, "Change color");
        swatch.Click += (_, _) => Show(_tags.SetColor(tag.Id, tag.Color + 1));
        row.Children.Add(swatch);

        var name = new TextBox { Text = tag.Name, MaxLength = TagSet.MaxNameLength };
        ClipboardText.Attach(name);
        void Commit()
        {
            if (name.Text.Trim().TrimStart('#').Trim() == tag.Name)
            {
                return;
            }

            if (Show(_tags.Rename(tag.Id, name.Text)))
            {
                name.Text = tag.Name; // rejected: put the old name back
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
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var delete = new HandButton
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Padding = new Thickness(7),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(delete, "Delete tag (removed from all tasks)");
        delete.Click += (_, _) => Show(_tags.Remove(tag.Id));
        Grid.SetColumn(delete, 2);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>Shows an error (or hides the line when there is none) and returns true when there was one.</summary>
    private bool Show(string? error)
    {
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        return error is not null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        _tags.TryAdd(NewName.Text, out var error);
        if (!Show(error))
        {
            NewName.Text = "";
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
}
