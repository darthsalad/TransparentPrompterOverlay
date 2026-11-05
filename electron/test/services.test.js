import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatDuration, tryParseDuration } from '../src/renderer/services/format.js';
import { LocalHeuristicRephraseService } from '../src/renderer/services/rephrase.js';
import { ScriptAlignmentService } from '../src/renderer/services/scriptAlignment.js';
import { getSelectionOrNearbyParagraph } from '../src/renderer/services/scriptSelection.js';

const SCRIPT = 'Welcome to the show. Today we talk about rockets.\n\n' +
    'First, the engines. Then we cover the fuel and the launch window.\n\n' +
    'Finally, we thank the crew for the launch.';

test('duration parsing matches the WPF rules', () => {
    assert.equal(tryParseDuration('03:30'), 210);
    assert.equal(tryParseDuration(' 2 : 05 '), 125);
    assert.equal(tryParseDuration('90'), 90);
    assert.equal(tryParseDuration('29'), null);
    assert.equal(tryParseDuration('00:29'), null);
    assert.equal(tryParseDuration('1:75'), null);
    assert.equal(tryParseDuration('abc'), null);
    assert.equal(formatDuration(210), '03:30');
    assert.equal(formatDuration(5400), '90:00');
});

test('alignment finds an exact multi-word match ahead of the cursor', () => {
    const service = new ScriptAlignmentService();
    service.updateScript(SCRIPT);
    const result = service.tryAlign('we cover the fuel', 0);
    assert.equal(result.characterIndex, SCRIPT.indexOf('fuel'));
    assert.equal(result.matchedFragment, 'we cover the fuel');
});

test('alignment prefers the repeated phrase nearest ahead of the current position', () => {
    const service = new ScriptAlignmentService();
    service.updateScript(SCRIPT);
    const firstLaunch = SCRIPT.indexOf('launch');
    const secondLaunch = SCRIPT.lastIndexOf('launch');

    assert.equal(service.tryAlign('for the launch', 0).characterIndex, secondLaunch);
    assert.equal(service.tryAlign('and the launch', 0).characterIndex, firstLaunch);
});

test('alignment ignores single words and unknown speech', () => {
    const service = new ScriptAlignmentService();
    service.updateScript(SCRIPT);
    assert.equal(service.tryAlign('rockets', 0), null);
    assert.equal(service.tryAlign('completely unrelated words', 0), null);
});

test('selection falls back to the current paragraph', () => {
    const caret = SCRIPT.indexOf('engines');
    const selection = getSelectionOrNearbyParagraph(SCRIPT, caret, 0);
    assert.equal(selection.label, 'Current paragraph');
    assert.equal(selection.text, 'First, the engines. Then we cover the fuel and the launch window.');
    assert.equal(SCRIPT.substr(selection.start, selection.length), selection.text);
});

test('selection uses explicit selected text', () => {
    const selection = getSelectionOrNearbyParagraph(SCRIPT, 0, 7);
    assert.deepEqual(selection, { start: 0, length: 7, text: 'Welcome', label: 'Selected text' });
});

test('rephrase produces simpler, shorter, and conversational versions', async () => {
    const service = new LocalHeuristicRephraseService();
    const suggestions = await service.rephrase('We will utilize numerous tools, and it is approximately done.');
    assert.deepEqual(suggestions.map((suggestion) => suggestion.title), ['Simpler wording', 'Shorter delivery', 'More conversational']);
    assert.equal(suggestions[0].text, 'We will use many tools, and it is about done.');
    assert.equal(suggestions[1].text, 'We will use many tools. Then it is about done.');
    assert.equal(suggestions[2].text, "We will use many tools, and it's about done.");
});

test('rephrase keeps capitalisation and punctuation around replaced words', async () => {
    const [simpler] = await new LocalHeuristicRephraseService().rephrase('"Utilize" this.');
    assert.equal(simpler.text, '"Use" this.');
});
