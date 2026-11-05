import { LocalHeuristicRephraseService } from './services/rephrase.js';
import { ScriptAlignmentService } from './services/scriptAlignment.js';
import { getSelectionOrNearbyParagraph } from './services/scriptSelection.js';
import { SpeechFollowService } from './services/speechFollow.js';
import { TeleprompterScrollService } from './services/teleprompterScroll.js';
import { clamp, formatDuration, isValidHexColor, tryParseDuration } from './services/format.js';

const prompter = window.prompter;
const $ = (id) => document.getElementById(id);

const elements = {
    glass: $('glass'),
    modeBadge: $('modeBadge'),
    playbackBadge: $('playbackBadge'),
    toggleClickThroughButton: $('toggleClickThroughButton'),
    minimalButton: $('minimalButton'),
    exitMinimalButton: $('exitMinimalButton'),
    minimalRestartButton: $('minimalRestartButton'),
    minimalSpeedSlider: $('minimalSpeedSlider'),
    minimalSpeedValue: $('minimalSpeedValue'),
    minimalPlayButton: $('minimalPlayButton'),
    minimalOpacitySlider: $('minimalOpacitySlider'),
    minimalOpacityValue: $('minimalOpacityValue'),
    hideFromCaptureCheckbox: $('hideFromCaptureCheckbox'),
    hideButton: $('hideButton'),
    resetButton: $('resetButton'),
    closeButton: $('closeButton'),
    modeSelect: $('modeSelect'),
    playbackButton: $('playbackButton'),
    restartButton: $('restartButton'),
    playbackStatus: $('playbackStatus'),
    voiceStatus: $('voiceStatus'),
    manualSpeedSlider: $('manualSpeedSlider'),
    manualSpeedValue: $('manualSpeedValue'),
    fasterButton: $('fasterButton'),
    slowerButton: $('slowerButton'),
    timedDurationInput: $('timedDurationInput'),
    timedDurationHelper: $('timedDurationHelper'),
    opacitySlider: $('opacitySlider'),
    opacityValue: $('opacityValue'),
    fontSizeSlider: $('fontSizeSlider'),
    fontSizeValue: $('fontSizeValue'),
    textColorSelect: $('textColorSelect'),
    alignmentSelect: $('alignmentSelect'),
    mirrorCheckbox: $('mirrorCheckbox'),
    widthSlider: $('widthSlider'),
    widthValue: $('widthValue'),
    heightSlider: $('heightSlider'),
    heightValue: $('heightValue'),
    rephraseButtons: document.querySelectorAll('.rephrase-button'),
    rephraseStatus: $('rephraseStatus'),
    rephraseSuggestions: $('rephraseSuggestions'),
    suggestionTemplate: $('suggestionTemplate'),
    prompt: $('prompt'),
    promptText: $('promptText'),
    editorPanel: $('editorPanel'),
    editor: $('editor'),
    statusLine: $('statusLine'),
    resizeGrip: $('resizeGrip')
};

const scrollService = new TeleprompterScrollService();
const speechFollowService = new SpeechFollowService(new ScriptAlignmentService(), {
    ensureModel: prompter.ensureSpeechModel,
    onModelProgress: prompter.onSpeechModelProgress
});
const rephraseService = new LocalHeuristicRephraseService();

const state = {
    settings: null,
    timedDurationHelperText: 'Use mm:ss or a number of seconds.',
    rephraseStatusText: 'No suggestions yet.',
    voiceStatusText: 'Voice-follow works best when you read the saved script clearly and stay fairly close to the original wording.',
    rephraseSuggestions: [],
    lastRephraseSelection: null,
    rephraseRequest: 0,
    currentPromptCharacterIndex: 0
};

let saveTimer = null;

// Scroll offset is tracked as a float so slow speeds do not stall on scrollTop rounding.
const viewport = {
    offset: 0,
    get currentOffset() {
        if (Math.abs(elements.prompt.scrollTop - this.offset) > 1) {
            this.offset = elements.prompt.scrollTop;
        }

        return this.offset;
    },
    get maxOffset() {
        return Math.max(0, elements.prompt.scrollHeight - elements.prompt.clientHeight);
    },
    scrollToOffset(offset) {
        this.offset = clamp(offset, 0, this.maxOffset);
        elements.prompt.scrollTop = this.offset;
        state.currentPromptCharacterIndex = estimateCharacterIndexFromOffset(this.offset);
    }
};

// ---- Derived labels (mirrors the WPF bindings) ----

const modeLabel = (mode) => ({ manual: 'Manual', timed: 'Timed' })[mode] ?? 'Voice-follow';

const isPlaybackActive = () => scrollService.isRunning || speechFollowService.isListening;

function playbackStatusText() {
    const { teleprompterMode: mode, manualScrollSpeed, timedDurationSeconds } = state.settings;
    if (mode === 'manual') {
        return scrollService.isRunning
            ? `Manual mode is scrolling at ${manualScrollSpeed.toFixed(0)} px/s.`
            : 'Manual mode waits for Start and moves at the speed you choose.';
    }

    if (mode === 'timed') {
        return scrollService.isRunning
            ? `Timed mode is pacing to finish in ${formatDuration(timedDurationSeconds)}.`
            : `Timed mode will finish in ${formatDuration(timedDurationSeconds)} once you press Start.`;
    }

    return speechFollowService.isListening
        ? 'Voice-follow is listening and will recenter the current line when speech matches the script.'
        : 'Voice-follow is paused. Press Start to listen for your place in the script.';
}

function statusLineText() {
    if (state.settings.clickThroughEnabled) {
        return 'Click-through is on. Press Ctrl+Shift+F8 to edit, move, or rephrase the script again.';
    }

    switch (state.settings.teleprompterMode) {
        case 'manual':
            return 'Manual mode keeps the stage glanceable while you control the pace directly.';
        case 'timed':
            return 'Timed mode spreads the remaining script across your target duration and is best for a known run length.';
        default:
            return 'Voice-follow is practical rather than perfect: it works best when you stay fairly close to the written script and use a clear mic.';
    }
}

function playbackButtonLabel() {
    if (state.settings.teleprompterMode === 'voiceFollow') {
        return speechFollowService.isListening ? 'Pause voice-follow' : 'Start voice-follow';
    }

    return scrollService.isRunning ? 'Pause scrolling' : 'Start scrolling';
}

// ---- Rendering ----

function setValue(element, value) {
    const text = String(value);
    if (element.value !== text) {
        element.value = text;
    }
}

function render() {
    const settings = state.settings;
    const rootStyle = document.documentElement.style;
    rootStyle.setProperty('--overlay-bg', `rgba(8, 14, 24, ${Math.round(settings.overlayOpacity * 255) / 255})`);
    rootStyle.setProperty('--prompt-color', settings.textColorHex);
    rootStyle.setProperty('--prompt-size', `${settings.fontSize}px`);
    rootStyle.setProperty('--prompt-align', settings.textAlignment);

    elements.glass.classList.toggle('minimal', settings.minimalMode);
    elements.glass.classList.toggle('click-through', settings.clickThroughEnabled);
    elements.prompt.classList.toggle('mirrored', settings.mirrorText);
    if (elements.promptText.textContent !== settings.scriptText) {
        elements.promptText.textContent = settings.scriptText;
    }

    const promptEditable = settings.minimalMode && !settings.clickThroughEnabled ? 'plaintext-only' : 'false';
    if (elements.promptText.contentEditable !== promptEditable) {
        elements.promptText.contentEditable = promptEditable;
    }

    elements.minimalPlayButton.classList.toggle('playing', isPlaybackActive());
    elements.minimalPlayButton.title = playbackButtonLabel();
    setValue(elements.minimalSpeedSlider, settings.manualScrollSpeed);
    elements.minimalSpeedSlider.disabled = settings.teleprompterMode !== 'manual';
    elements.minimalSpeedValue.textContent = settings.teleprompterMode === 'manual'
        ? `speed ${settings.manualScrollSpeed.toFixed(0)} px/s`
        : `${modeLabel(settings.teleprompterMode).toLowerCase()} mode`;
    setValue(elements.minimalOpacitySlider, settings.overlayOpacity);
    elements.minimalOpacityValue.textContent = `opacity ${Math.round(settings.overlayOpacity * 100)}%`;
    elements.hideFromCaptureCheckbox.checked = settings.hideFromCapture;

    setValue(elements.editor, settings.scriptText);
    elements.editorPanel.hidden = settings.clickThroughEnabled;

    elements.toggleClickThroughButton.textContent = settings.clickThroughEnabled ? 'Edit / unlock' : 'Enable click-through';
    elements.modeBadge.textContent = `${modeLabel(settings.teleprompterMode)} • ${settings.clickThroughEnabled ? 'click-through' : 'editable'}`;
    elements.playbackBadge.textContent = isPlaybackActive()
        ? 'playback active'
        : `${modeLabel(settings.teleprompterMode).toLowerCase()} ready`;

    setValue(elements.modeSelect, settings.teleprompterMode);
    elements.playbackButton.textContent = playbackButtonLabel();
    elements.playbackStatus.textContent = playbackStatusText();
    elements.voiceStatus.textContent = state.voiceStatusText;

    setValue(elements.manualSpeedSlider, settings.manualScrollSpeed);
    elements.manualSpeedValue.textContent = `${settings.manualScrollSpeed.toFixed(0)} px/s`;
    if (document.activeElement !== elements.timedDurationInput) {
        setValue(elements.timedDurationInput, formatDuration(settings.timedDurationSeconds));
    }

    elements.timedDurationHelper.textContent = state.timedDurationHelperText;

    setValue(elements.opacitySlider, settings.overlayOpacity);
    elements.opacityValue.textContent = `${Math.round(settings.overlayOpacity * 100)}%`;
    setValue(elements.fontSizeSlider, settings.fontSize);
    elements.fontSizeValue.textContent = settings.fontSize.toFixed(0);
    setValue(elements.textColorSelect, settings.textColorHex);
    setValue(elements.alignmentSelect, settings.textAlignment);
    elements.mirrorCheckbox.checked = settings.mirrorText;

    setValue(elements.widthSlider, Math.round(settings.windowWidth));
    elements.widthValue.textContent = settings.windowWidth.toFixed(0);
    setValue(elements.heightSlider, Math.round(settings.windowHeight));
    elements.heightValue.textContent = settings.windowHeight.toFixed(0);

    elements.rephraseStatus.textContent = state.rephraseStatusText;
    elements.statusLine.textContent = statusLineText();
}

function renderRephraseSuggestions() {
    elements.rephraseSuggestions.replaceChildren(...state.rephraseSuggestions.map((suggestion) => {
        const card = elements.suggestionTemplate.content.firstElementChild.cloneNode(true);
        card.querySelector('.suggestion-title').textContent = suggestion.title;
        card.querySelector('.suggestion-text').textContent = suggestion.text;
        card.querySelector('button').addEventListener('click', () => applySuggestion(suggestion));
        return card;
    }));
}

// ---- Settings ----

function normalizeSettings(settings) {
    settings.overlayOpacity = clamp(Number(settings.overlayOpacity) || 0.28, 0.12, 0.78);
    settings.fontSize = clamp(Number(settings.fontSize) || 34, 20, 76);
    const [minWidth, minHeight] = settings.minimalMode ? [320, 200] : [760, 520];
    settings.windowWidth = clamp(Number(settings.windowWidth) || 1120, minWidth, 1600);
    settings.windowHeight = clamp(Number(settings.windowHeight) || 780, minHeight, 980);
    settings.manualScrollSpeed = clamp(Number(settings.manualScrollSpeed) || 54, 10, 220);
    settings.timedDurationSeconds = clamp(Math.round(Number(settings.timedDurationSeconds) || 180), 30, 5400);

    if (typeof settings.scriptText !== 'string' || !settings.scriptText.trim()) {
        settings.scriptText = 'Paste or type your script here.';
    }

    if (!isValidHexColor(settings.textColorHex)) {
        settings.textColorHex = '#F8FAFC';
    }

    if (!['center', 'left', 'right'].includes(settings.textAlignment)) {
        settings.textAlignment = 'center';
    }

    if (!['manual', 'timed', 'voiceFollow'].includes(settings.teleprompterMode)) {
        settings.teleprompterMode = 'manual';
    }

    settings.mirrorText = Boolean(settings.mirrorText);
    settings.minimalMode = Boolean(settings.minimalMode);
    settings.hideFromCapture = settings.hideFromCapture !== false;
    settings.clickThroughEnabled = false;
    return settings;
}

function setSetting(key, value) {
    if (state.settings[key] === value) {
        return;
    }

    state.settings[key] = value;
    onSettingChanged(key);
    scheduleSave();
    render();
}

function onSettingChanged(key) {
    const settings = state.settings;
    switch (key) {
        case 'windowWidth':
        case 'windowHeight':
            prompter.setWindowSize(settings.windowWidth, settings.windowHeight);
            break;
        case 'scriptText':
            speechFollowService.updateScript(settings.scriptText);
            break;
        case 'clickThroughEnabled':
            prompter.setClickThrough(settings.clickThroughEnabled);
            if (!settings.clickThroughEnabled) {
                focusEditor();
            }

            break;
        case 'minimalMode':
            prompter.setMinimal(settings.minimalMode);
            keepReadingPositionAcrossLayoutChange();
            focusEditor();
            break;
        case 'hideFromCapture':
            prompter.setContentProtection(settings.hideFromCapture);
            break;
        case 'manualScrollSpeed':
            scrollService.manualSpeedPixelsPerSecond = settings.manualScrollSpeed;
            break;
        case 'timedDurationSeconds':
            scrollService.timedDurationMs = settings.timedDurationSeconds * 1000;
            break;
        case 'teleprompterMode':
            pauseAllPlayback();
            break;
    }
}

function scheduleSave() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => prompter.saveSettings({ ...state.settings }), 450);
}

// ---- Actions ----

function toggleClickThrough() {
    setSetting('clickThroughEnabled', !state.settings.clickThroughEnabled);
}

async function togglePlayback() {
    const mode = state.settings.teleprompterMode;
    if (mode === 'manual' || mode === 'timed') {
        await speechFollowService.stop();
        scrollService.manualSpeedPixelsPerSecond = state.settings.manualScrollSpeed;
        scrollService.timedDurationMs = state.settings.timedDurationSeconds * 1000;
        scrollService.toggle(mode);
    } else {
        scrollService.pause();
        if (speechFollowService.isListening) {
            await speechFollowService.stop();
        } else {
            const starting = speechFollowService.start(getApproximateCurrentCharacterIndex());
            render();
            await starting;
        }
    }

    render();
}

function adjustPlaybackRate(faster) {
    const settings = state.settings;
    switch (settings.teleprompterMode) {
        case 'manual':
            setSetting('manualScrollSpeed', clamp(settings.manualScrollSpeed + (faster ? 10 : -10), 10, 220));
            break;
        case 'timed':
            setSetting('timedDurationSeconds', clamp(settings.timedDurationSeconds + (faster ? -15 : 15), 30, 5400));
            state.timedDurationHelperText = `Timed mode is set to ${formatDuration(settings.timedDurationSeconds)}.`;
            if (scrollService.isRunning) {
                scrollService.start('timed');
            }

            render();
            break;
        default:
            state.voiceStatusText = 'Voice-follow tracks your speech rather than a manual speed setting.';
            render();
            break;
    }
}

function pauseAllPlayback() {
    scrollService.pause();
    speechFollowService.stop();
}

function startOverFromTop() {
    pauseAllPlayback();
    viewport.scrollToOffset(0);
    state.currentPromptCharacterIndex = 0;
    render();
}

function resetView() {
    const defaults = {
        overlayOpacity: 0.28,
        fontSize: 34,
        textColorHex: '#F8FAFC',
        textAlignment: 'center',
        mirrorText: false,
        windowWidth: 1120,
        windowHeight: 780,
        manualScrollSpeed: 54,
        timedDurationSeconds: 180,
        teleprompterMode: 'manual',
        clickThroughEnabled: false
    };

    for (const [key, value] of Object.entries(defaults)) {
        setSetting(key, value);
    }

    state.timedDurationHelperText = 'Timed mode will finish in 03:00.';
    viewport.scrollToOffset(0);
    render();
}

async function rephraseSelection() {
    const request = ++state.rephraseRequest;
    const { editor } = elements;
    state.lastRephraseSelection = getSelectionOrNearbyParagraph(
        state.settings.scriptText,
        editor.selectionStart,
        editor.selectionEnd - editor.selectionStart);

    const selection = state.lastRephraseSelection;
    if (!selection.text.trim()) {
        state.rephraseSuggestions = [];
        state.rephraseStatusText = 'Select text or place the caret inside a non-empty paragraph first.';
        renderRephraseSuggestions();
        render();
        return;
    }

    state.rephraseStatusText = `Rephrasing the ${selection.label.toLowerCase()}...`;
    render();

    const suggestions = await rephraseService.rephrase(selection.text);
    if (request !== state.rephraseRequest) {
        return;
    }

    state.rephraseSuggestions = suggestions;
    state.rephraseStatusText = suggestions.length === 0
        ? 'No alternatives were generated for that text.'
        : `Showing ${suggestions.length} easier-to-say alternatives for the ${selection.label.toLowerCase()}.`;
    renderRephraseSuggestions();
    render();
}

function applySuggestion(suggestion) {
    const selection = state.lastRephraseSelection;
    if (!selection || selection.length <= 0) {
        return;
    }

    const script = state.settings.scriptText;
    setSetting('scriptText', script.slice(0, selection.start) + suggestion.text + script.slice(selection.start + selection.length));

    elements.editor.focus();
    elements.editor.setSelectionRange(selection.start, selection.start + suggestion.text.length);
    state.lastRephraseSelection = { ...selection, length: suggestion.text.length, text: suggestion.text };
    state.rephraseStatusText = `${suggestion.title} applied to the script.`;
    render();
}

function focusEditor() {
    requestAnimationFrame(() => {
        if (state.settings.minimalMode) {
            return;
        }

        const { editor } = elements;
        editor.focus();
        editor.setSelectionRange(editor.value.length, editor.value.length);
    });
}

function keepReadingPositionAcrossLayoutChange() {
    const ratio = viewport.maxOffset > 0 ? viewport.currentOffset / viewport.maxOffset : 0;
    requestAnimationFrame(() => viewport.scrollToOffset(ratio * viewport.maxOffset));
}

// ---- Minimal-mode editing on the prompt stage ----

// Chromium may insert <br> or wrapper elements while editing; flatten them back to plain text.
function readEditableText(node) {
    let text = '';
    for (const child of node.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) {
            text += child.data;
        } else if (child.nodeName === 'BR') {
            text += '\n';
        } else {
            if (/^(DIV|P)$/.test(child.nodeName) && text && !text.endsWith('\n')) {
                text += '\n';
            }

            text += readEditableText(child);
        }
    }

    return text;
}

function onPromptEdited() {
    const { promptText } = elements;
    const text = readEditableText(promptText);
    const isSingleTextNode = promptText.childNodes.length === 1 && promptText.firstChild.nodeType === Node.TEXT_NODE;

    if (!isSingleTextNode && promptText.childNodes.length > 0) {
        const selection = getSelection();
        let caret = null;
        if (selection.rangeCount && promptText.contains(selection.focusNode)) {
            const range = document.createRange();
            range.setStart(promptText, 0);
            range.setEnd(selection.focusNode, selection.focusOffset);
            caret = readEditableText(range.cloneContents()).length;
        }

        promptText.textContent = text;
        if (caret !== null && promptText.firstChild) {
            selection.collapse(promptText.firstChild, Math.min(caret, text.length));
        }
    }

    setSetting('scriptText', text);
}

// ---- Prompt positioning ----

function centerPromptOnCharacter(characterIndex) {
    const script = state.settings.scriptText;
    const textNode = elements.promptText.firstChild;
    if (!script.length || !textNode) {
        return;
    }

    characterIndex = clamp(characterIndex, 0, Math.max(0, script.length - 1));
    const range = document.createRange();
    range.setStart(textNode, characterIndex);
    range.setEnd(textNode, Math.min(characterIndex + 1, textNode.length));
    const characterRect = range.getBoundingClientRect();

    let targetOffset;
    if (characterRect.height === 0) {
        targetOffset = viewport.maxOffset * (characterIndex / Math.max(1, script.length - 1));
    } else {
        const promptRect = elements.prompt.getBoundingClientRect();
        targetOffset = viewport.currentOffset + (characterRect.top - promptRect.top) -
            (elements.prompt.clientHeight / 2) + (characterRect.height / 2);
    }

    viewport.scrollToOffset(targetOffset);
    state.currentPromptCharacterIndex = characterIndex;
}

function estimateCharacterIndexFromOffset(offset) {
    const length = state.settings.scriptText.length;
    const maxOffset = viewport.maxOffset;
    if (length === 0 || maxOffset <= 0) {
        return 0;
    }

    return clamp(Math.round((offset / maxOffset) * (length - 1)), 0, length - 1);
}

function getApproximateCurrentCharacterIndex() {
    const length = state.settings.scriptText.length;
    if (length === 0) {
        return 0;
    }

    if (viewport.maxOffset <= 0) {
        return state.currentPromptCharacterIndex;
    }

    return estimateCharacterIndexFromOffset(viewport.currentOffset);
}

// ---- Window resize grip ----

function wireResizeGrip() {
    let start = null;
    let pendingFrame = null;

    elements.resizeGrip.addEventListener('pointerdown', (event) => {
        if (state.settings.clickThroughEnabled) {
            return;
        }

        elements.resizeGrip.setPointerCapture(event.pointerId);
        start = {
            x: event.screenX,
            y: event.screenY,
            width: state.settings.windowWidth,
            height: state.settings.windowHeight
        };
    });

    elements.resizeGrip.addEventListener('pointermove', (event) => {
        if (!start || pendingFrame) {
            return;
        }

        const { screenX, screenY } = event;
        pendingFrame = requestAnimationFrame(() => {
            pendingFrame = null;
            if (!start) {
                return;
            }

            prompter.setWindowSize(start.width + (screenX - start.x), start.height + (screenY - start.y));
        });
    });

    const endResize = () => {
        start = null;
    };
    elements.resizeGrip.addEventListener('pointerup', endResize);
    elements.resizeGrip.addEventListener('pointercancel', endResize);
}

// ---- Wiring ----

function wireEvents() {
    elements.toggleClickThroughButton.addEventListener('click', toggleClickThrough);
    elements.minimalButton.addEventListener('click', () => setSetting('minimalMode', true));
    elements.exitMinimalButton.addEventListener('click', () => setSetting('minimalMode', false));
    elements.minimalPlayButton.addEventListener('click', togglePlayback);
    elements.minimalRestartButton.addEventListener('click', startOverFromTop);
    elements.minimalSpeedSlider.addEventListener('input', () => setSetting('manualScrollSpeed', Number(elements.minimalSpeedSlider.value)));
    elements.minimalOpacitySlider.addEventListener('input', () => setSetting('overlayOpacity', Number(elements.minimalOpacitySlider.value)));
    elements.hideFromCaptureCheckbox.addEventListener('change', () => setSetting('hideFromCapture', elements.hideFromCaptureCheckbox.checked));
    elements.promptText.addEventListener('input', onPromptEdited);
    elements.hideButton.addEventListener('click', () => prompter.toggleVisibility());
    elements.resetButton.addEventListener('click', resetView);
    elements.closeButton.addEventListener('click', () => prompter.closeWindow());

    elements.modeSelect.addEventListener('change', () => setSetting('teleprompterMode', elements.modeSelect.value));
    elements.playbackButton.addEventListener('click', togglePlayback);
    elements.restartButton.addEventListener('click', () => {
        scrollService.restartFromTop();
        state.currentPromptCharacterIndex = 0;
    });

    elements.manualSpeedSlider.addEventListener('input', () => setSetting('manualScrollSpeed', Number(elements.manualSpeedSlider.value)));
    elements.fasterButton.addEventListener('click', () => adjustPlaybackRate(true));
    elements.slowerButton.addEventListener('click', () => adjustPlaybackRate(false));

    elements.timedDurationInput.addEventListener('input', () => {
        const totalSeconds = tryParseDuration(elements.timedDurationInput.value);
        if (totalSeconds !== null) {
            setSetting('timedDurationSeconds', totalSeconds);
            state.timedDurationHelperText = `Timed mode will finish in ${formatDuration(totalSeconds)}.`;
        } else {
            state.timedDurationHelperText = 'Use mm:ss, like 03:30, or enter total seconds.';
        }

        render();
    });
    elements.timedDurationInput.addEventListener('blur', render);

    elements.opacitySlider.addEventListener('input', () => setSetting('overlayOpacity', Number(elements.opacitySlider.value)));
    elements.fontSizeSlider.addEventListener('input', () => setSetting('fontSize', Number(elements.fontSizeSlider.value)));
    elements.textColorSelect.addEventListener('change', () => setSetting('textColorHex', elements.textColorSelect.value));
    elements.alignmentSelect.addEventListener('change', () => setSetting('textAlignment', elements.alignmentSelect.value));
    elements.mirrorCheckbox.addEventListener('change', () => setSetting('mirrorText', elements.mirrorCheckbox.checked));
    elements.widthSlider.addEventListener('input', () => setSetting('windowWidth', Number(elements.widthSlider.value)));
    elements.heightSlider.addEventListener('input', () => setSetting('windowHeight', Number(elements.heightSlider.value)));

    elements.rephraseButtons.forEach((button) => button.addEventListener('click', rephraseSelection));
    elements.editor.addEventListener('input', () => setSetting('scriptText', elements.editor.value));

    wireResizeGrip();

    scrollService.onPlaybackStateChanged = render;

    speechFollowService.onStatusChanged = (status) => {
        state.voiceStatusText = status;
        render();
    };

    speechFollowService.onFollowUpdate = (update) => {
        if (update.isSpeaking) {
            state.voiceStatusText = update.alignment
                ? `Hearing: “${update.transcriptFragment}” and matching it inside the script.`
                : `Hearing: “${update.transcriptFragment}”. Waiting for a stronger script match.`;
        } else {
            state.voiceStatusText = 'Voice-follow is waiting for you to continue speaking.';
        }

        if (state.settings.teleprompterMode === 'voiceFollow' && update.alignment) {
            centerPromptOnCharacter(update.alignment.characterIndex);
        }

        render();
    };

    prompter.onHotkey((action) => {
        switch (action) {
            case 'toggleClickThrough':
                toggleClickThrough();
                break;
            case 'togglePlayback':
                togglePlayback();
                break;
            case 'increaseScrollRate':
                adjustPlaybackRate(true);
                break;
            case 'decreaseScrollRate':
                adjustPlaybackRate(false);
                break;
        }
    });

    prompter.onWindowBounds(({ x, y, width, height }) => {
        Object.assign(state.settings, { windowLeft: x, windowTop: y, windowWidth: width, windowHeight: height });
        render();
    });

    prompter.onWindowShown(() => {
        if (!state.settings.clickThroughEnabled) {
            focusEditor();
        }
    });
}

async function init() {
    state.settings = normalizeSettings(await prompter.loadSettings());

    scrollService.manualSpeedPixelsPerSecond = state.settings.manualScrollSpeed;
    scrollService.timedDurationMs = state.settings.timedDurationSeconds * 1000;
    scrollService.attachViewport(viewport);
    speechFollowService.updateScript(state.settings.scriptText);

    prompter.setClickThrough(false);
    prompter.setMinimal(state.settings.minimalMode);
    prompter.setContentProtection(state.settings.hideFromCapture);
    wireEvents();
    render();
    focusEditor();
}

init();
