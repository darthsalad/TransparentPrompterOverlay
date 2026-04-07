using System.Text;
using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public sealed class LocalHeuristicRephraseService : ILineRephraseService
{
    private static readonly IReadOnlyDictionary<string, string> SimplerWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["utilize"] = "use",
        ["approximately"] = "about",
        ["demonstrate"] = "show",
        ["facilitate"] = "help",
        ["commence"] = "start",
        ["subsequently"] = "then",
        ["objective"] = "goal",
        ["numerous"] = "many",
        ["regarding"] = "about",
        ["additional"] = "extra",
        ["purchase"] = "buy",
        ["requirement"] = "need"
    };

    public Task<IReadOnlyList<RephraseSuggestion>> RephraseAsync(string text, CancellationToken cancellationToken = default)
    {
        var cleaned = text.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return Task.FromResult<IReadOnlyList<RephraseSuggestion>>([]);
        }

        var suggestions = new List<RephraseSuggestion>
        {
            new("Simpler wording", SimplifyVocabulary(cleaned)),
            new("Shorter delivery", BreakIntoShorterPhrases(cleaned)),
            new("More conversational", MakeMoreConversational(cleaned))
        };

        return Task.FromResult<IReadOnlyList<RephraseSuggestion>>(suggestions
            .DistinctBy(suggestion => suggestion.Text, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static string SimplifyVocabulary(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(ReplaceWordKeepingPunctuation);

        return string.Join(' ', words);
    }

    private static string BreakIntoShorterPhrases(string text)
    {
        var simplified = SimplifyVocabulary(text)
            .Replace("; ", ". ")
            .Replace(": ", ". ")
            .Replace(", and ", ". Then ")
            .Replace(", so ", ". So ")
            .Replace(", because ", ". Because ");

        return EnsureSentenceCase(simplified);
    }

    private static string MakeMoreConversational(string text)
    {
        var simplified = SimplifyVocabulary(text);
        var builder = new StringBuilder(simplified);

        builder.Replace("do not", "don't");
        builder.Replace("cannot", "can't");
        builder.Replace("I will", "I'll");
        builder.Replace("we will", "we'll");
        builder.Replace("it is", "it's");
        builder.Replace("that is", "that's");

        return EnsureSentenceCase(builder.ToString());
    }

    private static string ReplaceWordKeepingPunctuation(string token)
    {
        var leading = token.TakeWhile(character => !char.IsLetterOrDigit(character) && character != '\'').ToArray();
        var trailing = token.Reverse().TakeWhile(character => !char.IsLetterOrDigit(character) && character != '\'').Reverse().ToArray();
        var coreLength = token.Length - leading.Length - trailing.Length;
        if (coreLength <= 0)
        {
            return token;
        }

        var core = token.Substring(leading.Length, coreLength);
        var replacement = SimplerWords.TryGetValue(core, out var simpler) ? simpler : core;
        if (char.IsUpper(core[0]))
        {
            replacement = char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }

        return $"{new string(leading)}{replacement}{new string(trailing)}";
    }

    private static string EnsureSentenceCase(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? text
            : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
