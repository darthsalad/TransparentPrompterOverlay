import fs from 'node:fs';
import path from 'node:path';

export const DEFAULT_SETTINGS = Object.freeze({
    scriptText:
        'Paste or type your script here.\n\n' +
        'Manual: scroll yourself with the mouse wheel.\n' +
        'Speed: press play and it scrolls at the speed you set.\n' +
        'Voice: it listens and keeps your current line in the middle.',
    overlayOpacity: 0.28,
    fontSize: 34,
    textColorHex: '#F8FAFC',
    backgroundColorHex: '#080E18',
    textOutline: true,
    textAlignment: 'center',
    mirrorText: false,
    windowWidth: 1120,
    windowHeight: 780,
    windowLeft: null,
    windowTop: null,
    clickThroughEnabled: false,
    minimalMode: true,
    hideFromCapture: true,
    teleprompterMode: 'manual',
    manualScrollSpeed: 54,
    timedDurationSeconds: 180
});

export class SettingsStore {
    constructor(directory) {
        this.settingsPath = path.join(directory, 'settings.json');
    }

    load() {
        try {
            if (!fs.existsSync(this.settingsPath)) {
                return { ...DEFAULT_SETTINGS };
            }

            const stored = JSON.parse(fs.readFileSync(this.settingsPath, 'utf8'));
            return { ...DEFAULT_SETTINGS, ...stored };
        } catch {
            return { ...DEFAULT_SETTINGS };
        }
    }

    save(settings) {
        fs.mkdirSync(path.dirname(this.settingsPath), { recursive: true });
        fs.writeFileSync(this.settingsPath, JSON.stringify(settings, null, 2));
    }
}
