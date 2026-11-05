# Transparent Prompter Overlay (Electron)

Cross-platform port of the WPF overlay in the parent folder. It has the same features, layout, hotkeys, and behavior, and it runs on Windows and Linux.

## Run

```bash
cd electron
npm install
npm start          # Linux: runs under XWayland (--ozone-platform=x11)
npm test           # unit tests for alignment, rephrase, selection, duration parsing
```

On Windows, `npm start` works too. Windows ignores the `--ozone-platform` flag.

## Minimal mode

To enter minimal mode, click **Minimal mode** in the header. The script stage then fills the whole panel, and you can type or paste into it directly. The top bar holds, from left to right:
- **Drag**: drag it to move the window.
- **Manual / Speed / Voice**: Manual does not scroll on its own, so you scroll with the mouse wheel; pressing play switches to Speed. Speed scrolls at the slider's speed. Voice follows your speech. In Voice mode, a status line under the controls shows what the recognizer hears and any errors.
- **Click-through**: lets clicks pass to the apps behind. While it is on, the window ignores clicks, so a hint shows the shortcut to turn it off: `Ctrl+Shift+F8`.
- **Advanced**: leaves minimal mode.

A play/pause button sits at the bottom center, with a speed slider on its left and an opacity slider on its right. The speed slider only works in Speed mode. **Start over** at the bottom left pauses playback and scrolls back to the top; the script text is kept. Below a width of 560px, the buttons show icons only. In minimal mode the window can shrink to 320×200.

## Hide from screen sharing

The Window card has a **Hide from screen sharing and recordings** setting, on by default. It uses `setContentProtection`. On Windows 10 2004+ and macOS, the overlay stays visible to you but does not appear in screen shares, recordings or screenshots. Older Windows versions capture it as a black box. It has no effect on Linux.

## Build

```bash
npm run dist:win     # dist/Transparent Prompter Overlay 1.0.0.exe (portable) + NSIS installer
npm run build:speech # experimental Windows speech helper -> build/win-speech (needs the .NET 10 SDK; not shipped)
npm run dist:linux   # AppImage
```

You can build the Windows targets from Linux without wine. The builds are unsigned, so Windows SmartScreen will warn on first launch.

## Differences from the WPF version

| Area | WPF | Electron |
| --- | --- | --- |
| Voice-follow engine | `System.Speech` dictation (Windows only) | [Vosk](https://alphacephei.com/vosk/) offline WebAssembly (`vosk-browser`). Experimental, off by default: WinRT `Windows.Media.SpeechRecognition` through the `native/win-speech` helper, enabled with `PROMPTER_WINDOWS_SPEECH=1` |
| Speech model | Windows speech language pack | Windows: the system dictation engine; needs **Settings > Privacy & security > Speech > Online speech recognition** turned on. Vosk: `vosk-model-small-en-us-0.15` (about 40 MB), downloaded once on first voice-follow start |
| Click-through | `WS_EX_TRANSPARENT` | `BrowserWindow.setIgnoreMouseEvents` |
| Hotkeys | `RegisterHotKey` | `globalShortcut` (same `Ctrl+Shift+F8`–`F12`) |
| Settings | `%LocalAppData%\TransparentPrompterOverlay\settings.json` | `<userData>/settings.json` (Windows: `%APPDATA%\Transparent Prompter Overlay`, Linux: `~/.config/Transparent Prompter Overlay`) |

The app downloads the speech model into `<userData>/models/`. To use a different mirror, set `PROMPTER_VOSK_MODEL_URL`. Chromium's built-in Web Speech API is not used because it does not work inside Electron.

The app does not import settings from the WPF version.

## Linux notes

- `npm start` forces X11 (XWayland) because Wayland does not let apps keep a window always on top, place it, or grab global hotkeys.
- Under XWayland, global hotkeys fire only while an X11 window has focus. To try the GlobalShortcuts portal, run `npm run start:wayland`. Always-on-top does not work in that mode.
- Click-through does not work on COSMIC. Electron sets the X11 input shape to 1×1, but COSMIC still sends clicks to the overlay. Native Wayland has no click-through API. Click-through works on Windows.
- Tiling compositors such as COSMIC may auto-tile the window. To float it, press `Super+G` in COSMIC.
- Transparency needs a compositor. All current desktop environments include one.

## Layout

- `src/main.js`: window, `app://` protocol, hotkeys, click-through, bounds, speech-model download
- `src/preload.cjs`: the `window.prompter` IPC bridge
- `src/settingsStore.js`: JSON persistence
- `src/renderer/app.js`: UI wiring (port of `MainWindow.xaml.cs`)
- `src/renderer/services/`: ports of `Services/*.cs` (scroll, speech-follow, alignment, selection, rephrase)
