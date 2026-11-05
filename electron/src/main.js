import { app, BrowserWindow, dialog, globalShortcut, ipcMain, net, protocol, screen, session } from 'electron';
import fs from 'node:fs';
import path from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { SettingsStore } from './settingsStore.js';

const SOURCE_ROOT = path.dirname(fileURLToPath(import.meta.url));
const RENDERER_ROOT = path.join(SOURCE_ROOT, 'renderer');
const VOSK_SCRIPT = path.join(SOURCE_ROOT, '..', 'node_modules', 'vosk-browser', 'dist', 'vosk.js');
const APP_ORIGIN = 'app://prompter';

const SPEECH_MODEL_FILE = 'vosk-model-small-en-us-0.15.tar.gz';
const SPEECH_MODEL_URL = process.env.PROMPTER_VOSK_MODEL_URL ??
    `https://ccoreilly.github.io/vosk-browser/models/${SPEECH_MODEL_FILE}`;

const FULL_MIN_SIZE = { width: 760, height: 520 };
const MINIMAL_MIN_SIZE = { width: 320, height: 200 };

const HOTKEYS = [
    ['CommandOrControl+Shift+F8', 'toggleClickThrough'],
    ['CommandOrControl+Shift+F9', 'toggleVisibility'],
    ['CommandOrControl+Shift+F10', 'togglePlayback'],
    ['CommandOrControl+Shift+F11', 'increaseScrollRate'],
    ['CommandOrControl+Shift+F12', 'decreaseScrollRate']
];

protocol.registerSchemesAsPrivileged([
    {
        scheme: 'app',
        privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true, stream: true }
    }
]);

const settingsStore = new SettingsStore(app.getPath('userData'));
let settings = settingsStore.load();
let mainWindow = null;
let modelDownload = null;

function modelsDirectory() {
    return path.join(app.getPath('userData'), 'models');
}

function resolveInside(root, relativePath) {
    const resolved = path.resolve(root, `.${path.sep}${relativePath}`);
    return resolved.startsWith(root + path.sep) ? resolved : null;
}

function handleAppProtocol(request) {
    const { pathname } = new URL(request.url);
    const relativePath = decodeURIComponent(pathname).replace(/^\/+/, '') || 'index.html';

    let filePath;
    if (relativePath === 'vendor/vosk.js') {
        filePath = VOSK_SCRIPT;
    } else if (relativePath.startsWith('models/')) {
        filePath = resolveInside(modelsDirectory(), relativePath.slice('models/'.length));
    } else {
        filePath = resolveInside(RENDERER_ROOT, relativePath);
    }

    if (!filePath) {
        return new Response('Not found', { status: 404 });
    }

    return net.fetch(pathToFileURL(filePath).toString());
}

function minimumSize() {
    return settings.minimalMode ? MINIMAL_MIN_SIZE : FULL_MIN_SIZE;
}

function clampSize(width, height) {
    const { width: minWidth, height: minHeight } = minimumSize();
    const workArea = mainWindow
        ? screen.getDisplayMatching(mainWindow.getBounds()).workArea
        : screen.getPrimaryDisplay().workArea;

    return {
        width: Math.round(Math.min(Math.max(width, minWidth), Math.max(minWidth, workArea.width - 40))),
        height: Math.round(Math.min(Math.max(height, minHeight), Math.max(minHeight, workArea.height - 40)))
    };
}

function getStoredPosition(width, height) {
    if (typeof settings.windowLeft !== 'number' || typeof settings.windowTop !== 'number') {
        return {};
    }

    const stored = { x: settings.windowLeft, y: settings.windowTop, width, height };
    const visible = screen.getAllDisplays().some(({ bounds }) =>
        stored.x < bounds.x + bounds.width &&
        stored.x + stored.width > bounds.x &&
        stored.y < bounds.y + bounds.height &&
        stored.y + stored.height > bounds.y);

    return visible ? { x: Math.round(stored.x), y: Math.round(stored.y) } : {};
}

function sendBounds() {
    if (!mainWindow || mainWindow.isDestroyed()) {
        return;
    }

    const bounds = mainWindow.getBounds();
    settings = { ...settings, windowLeft: bounds.x, windowTop: bounds.y, windowWidth: bounds.width, windowHeight: bounds.height };
    mainWindow.webContents.send('window:bounds', bounds);
}

function toggleVisibility() {
    if (!mainWindow) {
        return;
    }

    if (mainWindow.isVisible()) {
        mainWindow.hide();
        return;
    }

    mainWindow.show();
    mainWindow.focus();
    mainWindow.setAlwaysOnTop(true, 'screen-saver');
    mainWindow.webContents.send('window:shown');
}

function registerHotkeys() {
    const failed = [];
    for (const [accelerator, action] of HOTKEYS) {
        const registered = globalShortcut.register(accelerator, () => {
            if (action === 'toggleVisibility') {
                toggleVisibility();
            } else {
                mainWindow?.webContents.send('hotkey', action);
            }
        });

        if (!registered) {
            failed.push(accelerator);
        }
    }

    if (failed.length > 0) {
        dialog.showMessageBox(mainWindow, {
            type: 'warning',
            title: 'Hotkey Registration',
            message: `Unable to register: ${failed.join(', ')}`,
            detail: 'The overlay will still work, but one or more hotkeys are already taken by another app.'
        });
    }
}

async function ensureSpeechModel(sender) {
    const modelPath = path.join(modelsDirectory(), SPEECH_MODEL_FILE);
    if (fs.existsSync(modelPath)) {
        return { url: `${APP_ORIGIN}/models/${SPEECH_MODEL_FILE}`, name: SPEECH_MODEL_FILE };
    }

    modelDownload ??= (async () => {
        fs.mkdirSync(modelsDirectory(), { recursive: true });
        const partialPath = `${modelPath}.part`;
        const response = await net.fetch(SPEECH_MODEL_URL);
        if (!response.ok || !response.body) {
            throw new Error(`Speech model download failed: HTTP ${response.status}`);
        }

        const totalBytes = Number(response.headers.get('content-length')) || 0;
        let receivedBytes = 0;
        const body = Readable.fromWeb(response.body);
        body.on('data', (chunk) => {
            receivedBytes += chunk.length;
            if (!sender.isDestroyed()) {
                sender.send('speech:model-progress', { receivedBytes, totalBytes });
            }
        });

        await pipeline(body, fs.createWriteStream(partialPath));
        fs.renameSync(partialPath, modelPath);
    })().finally(() => {
        modelDownload = null;
    });

    await modelDownload;
    return { url: `${APP_ORIGIN}/models/${SPEECH_MODEL_FILE}`, name: SPEECH_MODEL_FILE };
}

function createWindow() {
    const { width, height } = clampSize(
        Math.min(Number(settings.windowWidth) || 1120, 1600),
        Math.min(Number(settings.windowHeight) || 780, 980));

    mainWindow = new BrowserWindow({
        width,
        height,
        ...getStoredPosition(width, height),
        minWidth: minimumSize().width,
        minHeight: minimumSize().height,
        title: 'Transparent Prompter Overlay',
        frame: false,
        transparent: true,
        backgroundColor: '#00000000',
        hasShadow: false,
        alwaysOnTop: true,
        skipTaskbar: false,
        // Windows cannot edge-resize transparent frameless windows; the in-app grip resizes instead.
        resizable: process.platform !== 'win32',
        maximizable: false,
        fullscreenable: false,
        show: false,
        webPreferences: {
            preload: path.join(SOURCE_ROOT, 'preload.cjs'),
            contextIsolation: true,
            sandbox: true,
            nodeIntegration: false,
            spellcheck: true
        }
    });

    mainWindow.setAlwaysOnTop(true, 'screen-saver');
    // Windows 10 2004+ and macOS leave the window out of screen shares and recordings; no effect on Linux.
    mainWindow.setContentProtection(settings.hideFromCapture !== false);
    mainWindow.setMenu(null);
    mainWindow.once('ready-to-show', () => mainWindow.show());
    if (process.platform === 'linux') {
        // Some compositors (COSMIC) ignore the X11 "above" state, so raise again whenever another window takes focus.
        mainWindow.on('blur', () => {
            if (mainWindow.isVisible()) {
                mainWindow.setAlwaysOnTop(true, 'screen-saver');
                mainWindow.moveTop();
            }
        });
    }
    mainWindow.on('moved', sendBounds);
    mainWindow.on('resized', sendBounds);
    mainWindow.on('resize', sendBounds);
    mainWindow.on('close', () => {
        sendBounds();
        settingsStore.save(settings);
    });
    mainWindow.on('closed', () => {
        mainWindow = null;
    });

    mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    mainWindow.loadURL(`${APP_ORIGIN}/index.html`);

    if (process.env.PROMPTER_SMOKE_SCREENSHOT) {
        mainWindow.webContents.once('did-finish-load', () => {
            setTimeout(async () => {
                const image = await mainWindow.webContents.capturePage();
                fs.writeFileSync(process.env.PROMPTER_SMOKE_SCREENSHOT, image.toPNG());
                app.quit();
            }, 1500);
        });
    }
}

ipcMain.handle('settings:load', () => settings);

ipcMain.on('settings:save', (_event, nextSettings) => {
    settings = { ...settings, ...nextSettings };
    if (mainWindow) {
        const bounds = mainWindow.getBounds();
        settings = { ...settings, windowLeft: bounds.x, windowTop: bounds.y };
    }

    settingsStore.save(settings);
});

ipcMain.on('window:set-size', (_event, width, height) => {
    if (!mainWindow) {
        return;
    }

    const size = clampSize(width, height);
    const [currentWidth, currentHeight] = mainWindow.getSize();
    if (size.width !== currentWidth || size.height !== currentHeight) {
        mainWindow.setSize(size.width, size.height);
    }
});

ipcMain.on('window:set-minimal', (_event, enabled) => {
    settings = { ...settings, minimalMode: Boolean(enabled) };
    if (!mainWindow) {
        return;
    }

    const { width, height } = minimumSize();
    mainWindow.setMinimumSize(width, height);
    const [currentWidth, currentHeight] = mainWindow.getSize();
    if (currentWidth < width || currentHeight < height) {
        mainWindow.setSize(Math.max(currentWidth, width), Math.max(currentHeight, height));
    }
});

ipcMain.on('window:set-content-protection', (_event, enabled) => {
    mainWindow?.setContentProtection(Boolean(enabled));
});

ipcMain.on('window:set-click-through', (_event, enabled) => {
    mainWindow?.setIgnoreMouseEvents(Boolean(enabled));
});

ipcMain.on('window:toggle-visibility', toggleVisibility);

ipcMain.on('window:close', () => mainWindow?.close());

ipcMain.handle('speech:ensure-model', (event) => ensureSpeechModel(event.sender));

app.whenReady().then(() => {
    protocol.handle('app', handleAppProtocol);

    session.defaultSession.setPermissionRequestHandler((webContents, permission, callback) => {
        callback(permission === 'media' && webContents.getURL().startsWith(APP_ORIGIN));
    });
    session.defaultSession.setPermissionCheckHandler((_webContents, permission, requestingOrigin) =>
        permission === 'media' && requestingOrigin.startsWith(APP_ORIGIN));

    createWindow();
    registerHotkeys();
});

app.on('will-quit', () => {
    globalShortcut.unregisterAll();
});

app.on('window-all-closed', () => {
    app.quit();
});
