using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;

namespace BacpacGUI.Desktop.Behaviors;

public sealed class TextBoxAutoScrollBehavior
{
    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<TextBoxAutoScrollBehavior, TextBox, bool>("AutoScrollToEnd");

    static TextBoxAutoScrollBehavior()
    {
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((textBox, _) =>
        {
            if (!GetAutoScrollToEnd(textBox))
            {
                return;
            }

            textBox.CaretIndex = textBox.Text?.Length ?? 0;

            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = textBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer is not null)
                {
                    scrollViewer.Offset = scrollViewer.Offset.WithY(scrollViewer.Extent.Height);
                }
            });
        });
    }

    public static void SetAutoScrollToEnd(AvaloniaObject element, bool value)
    {
        element.SetValue(AutoScrollToEndProperty, value);
    }

    public static bool GetAutoScrollToEnd(AvaloniaObject element)
    {
        return element.GetValue(AutoScrollToEndProperty);
    }
}
