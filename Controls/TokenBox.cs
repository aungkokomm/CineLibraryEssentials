using CineLibraryEssentials.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CineLibraryEssentials.Controls;

/// <summary>
/// A list of values shown as chips (genres, studios, people...). Type and press
/// Enter to add, click × (or Backspace in the empty box) to remove. While
/// typing it suggests values already used in the library. Values are never
/// split on commas, so "Lions Gate Films, Inc." stays one name.
/// </summary>
public sealed class TokenBox : UserControl
{
    private readonly WrapPanel _panel = new();
    private readonly AutoSuggestBox _input;
    private readonly TextBlock _header;
    private readonly List<string> _items = new();

    /// <summary>Raised when the user adds or removes a value.</summary>
    public event EventHandler? Changed;

    /// <summary>Values offered while typing.</summary>
    public IReadOnlyList<string> Suggestions { get; set; } = Array.Empty<string>();

    public string Header
    {
        get => _header.Text;
        set
        {
            _header.Text = value;
            _header.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(_input, $"Add to {value}");
        }
    }

    public string PlaceholderText
    {
        get => _input.PlaceholderText;
        set => _input.PlaceholderText = value;
    }

    public TokenBox()
    {
        _header = new TextBlock { Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
        _input = new AutoSuggestBox
        {
            MinWidth = 180,
            PlaceholderText = "Type and press Enter",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _input.TextChanged += (s, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                s.ItemsSource = LibraryVocabulary.Match(Suggestions, s.Text, _items);
        };
        _input.QuerySubmitted += (s, e) =>
        {
            Add((e.ChosenSuggestion as string) ?? e.QueryText);
            s.Text = string.Empty;
            s.ItemsSource = null;
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Back && _input.Text.Length == 0 && _items.Count > 0)
            {
                RemoveAt(_items.Count - 1);
                e.Handled = true;
            }
        };

        var frame = new Border
        {
            BorderBrush = Brush("ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Background = Brush("ControlFillColorDefaultBrush"),
            Child = _panel,
        };
        var stack = new StackPanel();
        stack.Children.Add(_header);
        stack.Children.Add(frame);
        Content = stack;
        Rebuild();
    }

    /// <summary>The values, in order. Setting it doesn't raise Changed.</summary>
    public IReadOnlyList<string> Items
    {
        get => _items.ToList();
        set
        {
            _items.Clear();
            foreach (var v in value)
                if (!_items.Contains(v, StringComparer.OrdinalIgnoreCase)) _items.Add(v);
            Rebuild();
        }
    }

    private void Add(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || _items.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        _items.Add(value);
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
        _input.Focus(FocusState.Programmatic);
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
            _panel.Children.Add(Chip(_items[i], i));
        _panel.Children.Add(_input);
    }

    private FrameworkElement Chip(string value, int index)
    {
        var remove = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 9 },
            Padding = new Thickness(4),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(remove, $"Remove {value}");
        remove.Click += (_, _) => RemoveAt(index);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        row.Children.Add(remove);

        return new Border
        {
            Background = Brush("AccentFillColorTertiaryBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 2, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
    }

    private static Brush? Brush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }
}
