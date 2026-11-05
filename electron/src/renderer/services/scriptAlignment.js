const WORD_REGEX = /[\p{L}\p{Nd}']+/gu;

const normalize = (value) => value.trim().toLowerCase();

const distanceAhead = (candidateIndex, currentIndex) =>
    candidateIndex >= currentIndex
        ? candidateIndex - currentIndex
        : (currentIndex - candidateIndex) + 150;

export class ScriptAlignmentService {
    constructor() {
        this.wordIndex = new Map();
        this.scriptWords = [];
    }

    updateScript(script) {
        this.scriptWords = [];
        this.wordIndex = new Map();

        for (const match of script.matchAll(WORD_REGEX)) {
            const word = normalize(match[0]);
            if (!word) {
                continue;
            }

            const nextIndex = this.scriptWords.length;
            this.scriptWords.push({ word, characterIndex: match.index });

            if (!this.wordIndex.has(word)) {
                this.wordIndex.set(word, []);
            }

            this.wordIndex.get(word).push(nextIndex);
        }
    }

    tryAlign(fragment, currentCharacterIndex) {
        if (this.scriptWords.length === 0) {
            return null;
        }

        const fragmentWords = [...fragment.matchAll(WORD_REGEX)]
            .map((match) => normalize(match[0]))
            .filter(Boolean);

        if (fragmentWords.length < 2) {
            return null;
        }

        const currentWordIndex = this.getWordIndexForCharacter(currentCharacterIndex);
        const maxWindow = Math.min(fragmentWords.length, 8);

        for (let sequenceLength = maxWindow; sequenceLength >= 3; sequenceLength--) {
            const suffix = fragmentWords.slice(-sequenceLength);
            const exactEndIndex = this.findExactMatch(suffix, currentWordIndex);
            if (exactEndIndex !== null) {
                return this.createResult(exactEndIndex, suffix);
            }
        }

        const approximateEndIndex = this.findApproximateMatch(fragmentWords, currentWordIndex);
        return approximateEndIndex !== null
            ? this.createResult(approximateEndIndex, fragmentWords)
            : null;
    }

    findExactMatch(suffixWords, currentWordIndex) {
        const positions = this.wordIndex.get(suffixWords.at(-1));
        if (!positions) {
            return null;
        }

        let bestEndIndex = null;
        let bestDistance = Number.MAX_SAFE_INTEGER;

        for (const endIndex of positions) {
            const startIndex = endIndex - suffixWords.length + 1;
            if (startIndex < 0) {
                continue;
            }

            const withinPreferredWindow = endIndex >= Math.max(0, currentWordIndex - 20) &&
                endIndex <= Math.min(this.scriptWords.length - 1, currentWordIndex + 260);

            const matches = suffixWords.every((word, offset) => this.scriptWords[startIndex + offset].word === word);
            if (!matches) {
                continue;
            }

            let distance = distanceAhead(endIndex, currentWordIndex);
            if (!withinPreferredWindow) {
                distance += 1000;
            }

            if (distance < bestDistance) {
                bestDistance = distance;
                bestEndIndex = endIndex;
            }
        }

        return bestEndIndex;
    }

    findApproximateMatch(fragmentWords, currentWordIndex) {
        const positions = this.wordIndex.get(fragmentWords.at(-1));
        if (!positions) {
            return null;
        }

        let bestScore = 0;
        let bestEndIndex = null;

        for (const endIndex of positions) {
            if (endIndex < Math.max(0, currentWordIndex - 30) ||
                endIndex > Math.min(this.scriptWords.length - 1, currentWordIndex + 320)) {
                continue;
            }

            let score = 0;
            let fragmentCursor = fragmentWords.length - 1;
            let scriptCursor = endIndex;

            while (fragmentCursor >= 0 && scriptCursor >= 0) {
                if (this.scriptWords[scriptCursor].word !== fragmentWords[fragmentCursor]) {
                    break;
                }

                score++;
                fragmentCursor--;
                scriptCursor--;
            }

            if (score > bestScore) {
                bestScore = score;
                bestEndIndex = endIndex;
            }
        }

        return bestScore >= 2 ? bestEndIndex : null;
    }

    createResult(endIndex, matchedWords) {
        return {
            characterIndex: this.scriptWords[endIndex].characterIndex,
            progressRatio: (endIndex + 1) / this.scriptWords.length,
            matchedFragment: matchedWords.join(' ')
        };
    }

    getWordIndexForCharacter(characterIndex) {
        if (this.scriptWords.length === 0) {
            return 0;
        }

        let low = 0;
        let high = this.scriptWords.length - 1;

        while (low <= high) {
            const mid = Math.floor((low + high) / 2);
            if (this.scriptWords[mid].characterIndex <= characterIndex) {
                low = mid + 1;
            } else {
                high = mid - 1;
            }
        }

        return Math.min(Math.max(high, 0), this.scriptWords.length - 1);
    }
}
