using System.Windows;

namespace TransparentPrompterOverlay.Models;

public sealed record TextAlignmentOption(string Name, TextAlignment Value)
{
    public static IReadOnlyList<TextAlignmentOption> CreateDefaults() =>
    [
        new("Center", TextAlignment.Center),
        new("Left", TextAlignment.Left),
        new("Right", TextAlignment.Right)
    ];
}
