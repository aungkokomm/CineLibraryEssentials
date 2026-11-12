using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryEssentials.Controls;

/// <summary>
/// Turns an AutoSuggestBox into a list to pick from that you can also type into:
/// the whole list opens when you click into it or on its arrow (or press Enter on
/// a value from the list), typing narrows it, and any text counts. Used instead of an editable ComboBox, which doesn't always
/// take a mouse click on an item.
/// </summary>
public static class PickList
{
    public static void Attach(AutoSuggestBox box, IReadOnlyList<string> items, Action? changed = null)
    {
        box.QueryIcon = new FontIcon { Glyph = "\uE70D", FontSize = 12 };
        // Only a click opens it: not Tab, and not the focus coming back after a pick
        box.GotFocus += (_, e) =>
        {
            if (e.OriginalSource is Control { FocusState: FocusState.Pointer }) ShowAll(box, items);
        };
        box.TextChanged += (sender, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var text = sender.Text.Trim();
                sender.ItemsSource = text.Length == 0
                    ? items
                    : items.Where(i => i.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            changed?.Invoke();
        };
        // The arrow (or Enter) on a value from the list opens the list to change it;
        // Enter after typing your own value just keeps it
        box.QuerySubmitted += (sender, args) =>
        {
            var text = args.QueryText.Trim();
            if (args.ChosenSuggestion == null && !sender.IsSuggestionListOpen
                && (text.Length == 0 || items.Contains(text, StringComparer.OrdinalIgnoreCase)))
                ShowAll(sender, items);
        };
    }

    private static void ShowAll(AutoSuggestBox box, IReadOnlyList<string> items)
    {
        box.ItemsSource = items;
        box.IsSuggestionListOpen = true;
    }
}
