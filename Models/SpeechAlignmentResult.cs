namespace TransparentPrompterOverlay.Models;

public sealed record SpeechAlignmentResult(int CharacterIndex, double ProgressRatio, string MatchedFragment);
