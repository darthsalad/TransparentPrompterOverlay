namespace TransparentPrompterOverlay.Models;

public sealed record SpeechFollowUpdate(
    bool IsSpeaking,
    string TranscriptFragment,
    SpeechAlignmentResult? Alignment);
