using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyConnect.Helpers;

/// <summary>Small pixel-based wheel steps, including nested device lists.</summary>
public static class SlowScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SlowScroll), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element) return;
        if ((bool)args.NewValue) element.PreviewMouseWheel += OnMouseWheel;
        else element.PreviewMouseWheel -= OnMouseWheel;
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || args.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
        // Find the innermost scrollable view under the pointer; at its boundary, scroll the page.
        for (var node = args.OriginalSource as DependencyObject; node is not null; node = Parent(node))
        {
            if (node is not ScrollViewer viewer || viewer.ScrollableHeight <= 0) continue;
            if (args.Delta > 0 && viewer.VerticalOffset <= 0 ||
                args.Delta < 0 && viewer.VerticalOffset >= viewer.ScrollableHeight) continue;
            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset - args.Delta / 120.0 * 24,
                0, viewer.ScrollableHeight));
            args.Handled = true;
            return;
        }
    }

    private static DependencyObject? Parent(DependencyObject node) => node is Visual
        ? VisualTreeHelper.GetParent(node)
        : LogicalTreeHelper.GetParent(node);
}
