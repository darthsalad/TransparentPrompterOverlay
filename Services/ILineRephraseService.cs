using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public interface ILineRephraseService
{
    Task<IReadOnlyList<RephraseSuggestion>> RephraseAsync(string text, CancellationToken cancellationToken = default);
}
