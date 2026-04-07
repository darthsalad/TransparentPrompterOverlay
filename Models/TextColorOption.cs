namespace TransparentPrompterOverlay.Models;

public sealed record TextColorOption(string Name, string Hex)
{
    public static IReadOnlyList<TextColorOption> CreateDefaults() =>
    [
        new("Studio White", "#F8FAFC"),
        new("Soft Amber", "#FDE68A"),
        new("Mint Glass", "#D1FAE5"),
        new("Sky Cue", "#BFDBFE"),
        new("Rose Cue", "#FBCFE8"),
        new("Slate Ink", "#CBD5E1")
    ];
}
