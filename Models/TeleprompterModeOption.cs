namespace TransparentPrompterOverlay.Models;

public sealed record TeleprompterModeOption(string Name, TeleprompterMode Value)
{
    public static IReadOnlyList<TeleprompterModeOption> CreateDefaults() =>
    [
        new("Manual", TeleprompterMode.Manual),
        new("Timed", TeleprompterMode.Timed),
        new("Voice-follow", TeleprompterMode.VoiceFollow)
    ];
}
