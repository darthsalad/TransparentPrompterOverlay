function findParagraphBoundary(script, index, searchBackward) {
    if (searchBackward) {
        for (let cursor = Math.min(Math.max(index - 1, 0), script.length - 1); cursor > 0; cursor--) {
            if (script[cursor] === '\n' && script[cursor - 1] === '\n') {
                return cursor + 1;
            }
        }

        return 0;
    }

    for (let cursor = Math.min(Math.max(index, 0), script.length); cursor < script.length - 1; cursor++) {
        if (script[cursor] === '\n' && script[cursor + 1] === '\n') {
            return cursor;
        }
    }

    return script.length;
}

export function getSelectionOrNearbyParagraph(script, selectionStart, selectionLength) {
    if (!script) {
        return { start: 0, length: 0, text: '', label: 'Nothing selected' };
    }

    if (selectionLength > 0) {
        return {
            start: selectionStart,
            length: selectionLength,
            text: script.substring(selectionStart, selectionStart + selectionLength),
            label: 'Selected text'
        };
    }

    const safeCursor = Math.min(Math.max(selectionStart, 0), script.length);
    const paragraphStart = findParagraphBoundary(script, safeCursor, true);
    const paragraphEnd = findParagraphBoundary(script, safeCursor, false);
    const paragraphText = script.substring(paragraphStart, paragraphEnd).trim();

    if (paragraphText) {
        return { start: paragraphStart, length: paragraphEnd - paragraphStart, text: paragraphText, label: 'Current paragraph' };
    }

    let lineStart = script.lastIndexOf('\n', Math.max(0, safeCursor - 1));
    lineStart = lineStart < 0 ? 0 : lineStart + 1;
    let lineEnd = script.indexOf('\n', safeCursor);
    lineEnd = lineEnd < 0 ? script.length : lineEnd;

    return {
        start: lineStart,
        length: lineEnd - lineStart,
        text: script.substring(lineStart, lineEnd).trim(),
        label: 'Current line'
    };
}
