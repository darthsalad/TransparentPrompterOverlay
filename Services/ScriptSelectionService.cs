using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public static class ScriptSelectionService
{
    public static ScriptSelection GetSelectionOrNearbyParagraph(string script, int selectionStart, int selectionLength)
    {
        if (string.IsNullOrEmpty(script))
        {
            return new ScriptSelection(0, 0, string.Empty, "Nothing selected");
        }

        if (selectionLength > 0)
        {
            var selectedText = script.Substring(selectionStart, selectionLength);
            return new ScriptSelection(selectionStart, selectionLength, selectedText, "Selected text");
        }

        var safeCursor = Math.Clamp(selectionStart, 0, script.Length);
        var paragraphStart = FindParagraphBoundary(script, safeCursor, searchBackward: true);
        var paragraphEnd = FindParagraphBoundary(script, safeCursor, searchBackward: false);
        var paragraphText = script.Substring(paragraphStart, paragraphEnd - paragraphStart).Trim();

        if (!string.IsNullOrWhiteSpace(paragraphText))
        {
            return new ScriptSelection(paragraphStart, paragraphEnd - paragraphStart, paragraphText, "Current paragraph");
        }

        var lineStart = script.LastIndexOf('\n', Math.Max(0, safeCursor - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = script.IndexOf('\n', safeCursor);
        lineEnd = lineEnd < 0 ? script.Length : lineEnd;
        var lineText = script.Substring(lineStart, lineEnd - lineStart).Trim();

        return new ScriptSelection(lineStart, lineEnd - lineStart, lineText, "Current line");
    }

    private static int FindParagraphBoundary(string script, int index, bool searchBackward)
    {
        if (searchBackward)
        {
            var cursor = Math.Clamp(index - 1, 0, script.Length - 1);
            for (; cursor > 0; cursor--)
            {
                if (script[cursor] == '\n' && cursor > 0 && script[cursor - 1] == '\n')
                {
                    return cursor + 1;
                }
            }

            return 0;
        }

        var forwardCursor = Math.Clamp(index, 0, script.Length);
        for (; forwardCursor < script.Length - 1; forwardCursor++)
        {
            if (script[forwardCursor] == '\n' && script[forwardCursor + 1] == '\n')
            {
                return forwardCursor;
            }
        }

        return script.Length;
    }
}
