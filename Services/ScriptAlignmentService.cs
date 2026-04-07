using System.Text.RegularExpressions;
using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public sealed class ScriptAlignmentService
{
    private static readonly Regex WordRegex = new("[\\p{L}\\p{Nd}']+", RegexOptions.Compiled);

    private readonly Dictionary<string, List<int>> _wordIndex = new(StringComparer.Ordinal);
    private readonly List<(string Word, int CharacterIndex)> _scriptWords = new();

    public void UpdateScript(string script)
    {
        _scriptWords.Clear();
        _wordIndex.Clear();

        foreach (Match match in WordRegex.Matches(script))
        {
            var normalized = Normalize(match.Value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var nextIndex = _scriptWords.Count;
            _scriptWords.Add((normalized, match.Index));

            if (!_wordIndex.TryGetValue(normalized, out var positions))
            {
                positions = [];
                _wordIndex[normalized] = positions;
            }

            positions.Add(nextIndex);
        }
    }

    public SpeechAlignmentResult? TryAlign(string fragment, int currentCharacterIndex)
    {
        if (_scriptWords.Count == 0)
        {
            return null;
        }

        var fragmentWords = WordRegex.Matches(fragment)
            .Select(match => Normalize(match.Value))
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();

        if (fragmentWords.Length < 2)
        {
            return null;
        }

        var currentWordIndex = GetWordIndexForCharacter(currentCharacterIndex);
        var maxWindow = Math.Min(fragmentWords.Length, 8);

        for (var sequenceLength = maxWindow; sequenceLength >= 3; sequenceLength--)
        {
            var suffix = fragmentWords[^sequenceLength..];
            var exactMatch = FindExactMatch(suffix, currentWordIndex);
            if (exactMatch is int exactEndIndex)
            {
                return CreateResult(exactEndIndex, suffix);
            }
        }

        var approximateEndIndex = FindApproximateMatch(fragmentWords, currentWordIndex);
        return approximateEndIndex is int endIndex
            ? CreateResult(endIndex, fragmentWords)
            : null;
    }

    private int? FindExactMatch(string[] suffixWords, int currentWordIndex)
    {
        var lastWord = suffixWords[^1];
        if (!_wordIndex.TryGetValue(lastWord, out var positions))
        {
            return null;
        }

        int? bestEndIndex = null;
        var bestDistance = int.MaxValue;

        foreach (var endIndex in positions)
        {
            var startIndex = endIndex - suffixWords.Length + 1;
            if (startIndex < 0)
            {
                continue;
            }

            var withinPreferredWindow = endIndex >= Math.Max(0, currentWordIndex - 20) &&
                                        endIndex <= Math.Min(_scriptWords.Count - 1, currentWordIndex + 260);

            var matches = true;
            for (var offset = 0; offset < suffixWords.Length; offset++)
            {
                if (_scriptWords[startIndex + offset].Word != suffixWords[offset])
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            var distance = DistanceAhead(endIndex, currentWordIndex);
            if (!withinPreferredWindow)
            {
                distance += 1000;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestEndIndex = endIndex;
            }
        }

        return bestEndIndex;
    }

    private int? FindApproximateMatch(string[] fragmentWords, int currentWordIndex)
    {
        var lastWord = fragmentWords[^1];
        if (!_wordIndex.TryGetValue(lastWord, out var positions))
        {
            return null;
        }

        var bestScore = 0;
        int? bestEndIndex = null;

        foreach (var endIndex in positions)
        {
            if (endIndex < Math.Max(0, currentWordIndex - 30) ||
                endIndex > Math.Min(_scriptWords.Count - 1, currentWordIndex + 320))
            {
                continue;
            }

            var score = 0;
            var fragmentCursor = fragmentWords.Length - 1;
            var scriptCursor = endIndex;

            while (fragmentCursor >= 0 && scriptCursor >= 0)
            {
                if (_scriptWords[scriptCursor].Word != fragmentWords[fragmentCursor])
                {
                    break;
                }

                score++;
                fragmentCursor--;
                scriptCursor--;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestEndIndex = endIndex;
            }
        }

        return bestScore >= 2 ? bestEndIndex : null;
    }

    private SpeechAlignmentResult CreateResult(int endIndex, IReadOnlyList<string> matchedWords)
    {
        var charIndex = _scriptWords[endIndex].CharacterIndex;
        var progressRatio = (endIndex + 1d) / _scriptWords.Count;
        return new SpeechAlignmentResult(charIndex, progressRatio, string.Join(" ", matchedWords));
    }

    private int GetWordIndexForCharacter(int characterIndex)
    {
        if (_scriptWords.Count == 0)
        {
            return 0;
        }

        var low = 0;
        var high = _scriptWords.Count - 1;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_scriptWords[mid].CharacterIndex <= characterIndex)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Math.Clamp(high, 0, _scriptWords.Count - 1);
    }

    private static int DistanceAhead(int candidateIndex, int currentIndex)
    {
        return candidateIndex >= currentIndex
            ? candidateIndex - currentIndex
            : (currentIndex - candidateIndex) + 150;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
