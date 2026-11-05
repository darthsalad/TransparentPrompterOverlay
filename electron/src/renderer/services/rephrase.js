const SIMPLER_WORDS = new Map([
    ['utilize', 'use'],
    ['approximately', 'about'],
    ['demonstrate', 'show'],
    ['facilitate', 'help'],
    ['commence', 'start'],
    ['subsequently', 'then'],
    ['objective', 'goal'],
    ['numerous', 'many'],
    ['regarding', 'about'],
    ['additional', 'extra'],
    ['purchase', 'buy'],
    ['requirement', 'need']
]);

const isUpper = (character) => character !== character.toLowerCase() && character === character.toUpperCase();

function replaceWordKeepingPunctuation(token) {
    const leading = token.match(/^[^\p{L}\p{Nd}']*/u)[0];
    const rest = token.slice(leading.length);
    const trailing = rest.match(/[^\p{L}\p{Nd}']*$/u)[0];
    const core = rest.slice(0, rest.length - trailing.length);
    if (core.length === 0) {
        return token;
    }

    let replacement = SIMPLER_WORDS.get(core.toLowerCase()) ?? core;
    if (isUpper(core[0])) {
        replacement = replacement[0].toUpperCase() + replacement.slice(1);
    }

    return `${leading}${replacement}${trailing}`;
}

const ensureSentenceCase = (text) =>
    text.trim().length === 0 ? text : text[0].toUpperCase() + text.slice(1);

const simplifyVocabulary = (text) =>
    text.split(' ').filter(Boolean).map(replaceWordKeepingPunctuation).join(' ');

function breakIntoShorterPhrases(text) {
    const simplified = simplifyVocabulary(text)
        .replaceAll('; ', '. ')
        .replaceAll(': ', '. ')
        .replaceAll(', and ', '. Then ')
        .replaceAll(', so ', '. So ')
        .replaceAll(', because ', '. Because ');

    return ensureSentenceCase(simplified);
}

function makeMoreConversational(text) {
    const conversational = simplifyVocabulary(text)
        .replaceAll('do not', "don't")
        .replaceAll('cannot', "can't")
        .replaceAll('I will', "I'll")
        .replaceAll('we will', "we'll")
        .replaceAll('it is', "it's")
        .replaceAll('that is', "that's");

    return ensureSentenceCase(conversational);
}

// Mirrors ILineRephraseService: async so an API-backed provider can drop in later.
export class LocalHeuristicRephraseService {
    async rephrase(text) {
        const cleaned = text.trim();
        if (!cleaned) {
            return [];
        }

        const suggestions = [
            { title: 'Simpler wording', text: simplifyVocabulary(cleaned) },
            { title: 'Shorter delivery', text: breakIntoShorterPhrases(cleaned) },
            { title: 'More conversational', text: makeMoreConversational(cleaned) }
        ];

        const seen = new Set();
        return suggestions.filter(({ text: suggestion }) => {
            const key = suggestion.toLowerCase();
            if (seen.has(key)) {
                return false;
            }

            seen.add(key);
            return true;
        });
    }
}
