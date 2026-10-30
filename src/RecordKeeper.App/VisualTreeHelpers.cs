using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RecordKeeper.App;

internal static class VisualTreeHelpers
{
    /// <summary>Walks the visual tree looking for the first descendant matching a test.</summary>
    public static T? FindDescendant<T>(DependencyObject? root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        if (root is null) return null;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed && (predicate is null || predicate(typed))) return typed;

            var deeper = FindDescendant(child, predicate);
            if (deeper is not null) return deeper;
        }

        return null;
    }

    /// <summary>
    /// Finds the input control inside a field's row. The visible one is the right one,
    /// since the template collapses the three editors that do not apply.
    /// </summary>
    public static Control? FindFieldInput(DependencyObject? row) =>
        FindDescendant<Control>(row, control =>
            control.Tag as string == "FieldInput" && control.IsVisible);

    /// <summary>
    /// Moves keyboard focus to an element and makes sure it is scrolled into view, so
    /// Windows Magnifier follows along. Returns false when the element cannot take focus.
    /// </summary>
    public static bool FocusAccessibly(FrameworkElement? element)
    {
        if (element is null || !element.IsVisible || !element.IsEnabled) return false;

        element.BringIntoView();
        return element.Focus() || element.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>True when the keyboard is in a control where typing should win over shortcuts.</summary>
    public static bool IsTypingInTextBox()
    {
        var focused = Keyboard.FocusedElement;
        return focused is TextBox or PasswordBox ||
               (focused is ComboBox { IsEditable: true });
    }
}
