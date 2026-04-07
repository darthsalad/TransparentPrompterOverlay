using System.Windows;
using System.Windows.Media;

namespace TransparentPrompterOverlay.Services;

public static class VisualTreeUtilities
{
    public static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedResult = FindDescendant<T>(child);
            if (nestedResult is not null)
            {
                return nestedResult;
            }
        }

        return null;
    }
}
