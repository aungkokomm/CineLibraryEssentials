using CineLibraryEssentials.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Attached property that lets a TextBlock render a list of <see cref="DiffSegment"/>
/// as inline runs, with strikethrough + red color for "removed" segments and a muted
/// grey color for kept segments. Used to show a word-level diff between original
/// and cleaned filenames.
/// </summary>
public static class RichTextBehavior
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IEnumerable<DiffSegment>),
            typeof(RichTextBehavior),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static IEnumerable<DiffSegment>? GetSegments(DependencyObject obj)
        => (IEnumerable<DiffSegment>?)obj.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject obj, IEnumerable<DiffSegment>? value)
        => obj.SetValue(SegmentsProperty, value);

    private static readonly SolidColorBrush RemovedBrush =
        new(Color.FromArgb(255, 196, 43, 28));

    private static readonly SolidColorBrush KeptBrush =
        new(Color.FromArgb(180, 130, 130, 130));

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IEnumerable<DiffSegment> segments) return;

        foreach (var seg in segments)
        {
            var run = new Run { Text = seg.Text };
            if (seg.IsRemoved)
            {
                run.TextDecorations = TextDecorations.Strikethrough;
                run.Foreground = RemovedBrush;
            }
            else
            {
                run.Foreground = KeptBrush;
            }
            tb.Inlines.Add(run);
        }
    }
}
