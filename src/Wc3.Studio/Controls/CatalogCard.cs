using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wc3.Studio.Controls;

/// <summary>
/// Builds one standard catalog card (used by the regions, cameras and sounds panels): a bold
/// title, a column of read-only "label: value" rows, an optional inline field editor (a field
/// dropdown + value box + Apply, over the type's editable fields), and an optional Remove
/// button. Kept in one place so the three catalog panels never re-declare the same card body.
/// All controls are built in code and live in a non-virtualized list, so the editable box is
/// safe from the ListBox-recycling pitfall.
/// </summary>
public static class CatalogCard
{
    private static readonly IBrush DimBrush = StudioPalette.Muted;

    public static Border Build(
        string title,
        IEnumerable<(string Label, string Value)> fields,
        IReadOnlyList<string>? editableFields = null,
        Action<string, string>? onSetField = null,
        Action? onRemove = null)
    {
        var body = new StackPanel { Spacing = 4 };

        // Title row, with the Remove button pushed to the right.
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(titleText, 0);
        titleRow.Children.Add(titleText);
        if (onRemove is not null)
        {
            var remove = new Button
            {
                Content = "Remove",
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(remove, "Delete this entry from the map");
            remove.Click += (_, _) => onRemove();
            Grid.SetColumn(remove, 1);
            titleRow.Children.Add(remove);
        }
        body.Children.Add(titleRow);

        foreach (var (label, value) in fields)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{label}: {value}",
                Foreground = DimBrush,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        // Inline field editor: pick a field, type a value, Apply.
        if (editableFields is { Count: > 0 } && onSetField is not null)
        {
            var editor = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            var picker = new ComboBox
            {
                ItemsSource = editableFields,
                SelectedIndex = 0,
                MinWidth = 110,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(picker, 0);
            editor.Children.Add(picker);

            var valueBox = new TextBox
            {
                Watermark = "value",
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(valueBox, 1);
            editor.Children.Add(valueBox);

            var apply = new Button
            {
                Content = "Apply",
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            apply.Click += (_, _) =>
            {
                if (picker.SelectedItem is string field)
                    onSetField(field, valueBox.Text ?? "");
            };
            Grid.SetColumn(apply, 2);
            editor.Children.Add(apply);

            body.Children.Add(editor);
        }

        return new Card { Child = body };
    }
}
