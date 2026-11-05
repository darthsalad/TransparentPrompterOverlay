# Transparent Prompter Overlay

A transparent, always-on-top teleprompter overlay. The repo has two implementations:

- **WPF (repo root):** the original. Windows only (`net8.0-windows`, `System.Speech`). It is the reference for behavior and is not actively developed.
- **Electron (`electron/`):** a cross-platform port with the same features plus minimal mode and screen-capture hiding. All new work happens here.

Electron details, run/build commands and the feature list are in `electron/README.md`.

## Commands (run in `electron/`)

```bash
npm install
npm start            # Linux: forces XWayland (--ozone-platform=x11). Windows ignores that flag.
npm test             # node --test: alignment, rephrase, selection, duration parsing
npm run dist:win     # portable .exe + NSIS installer in dist/ (works from Linux without wine)
npm run dist:linux   # AppImage
```

## Architecture (Electron)

- `src/main.js`: window creation, the privileged `app://prompter` protocol, global hotkeys, click-through, content protection, bounds persistence, and the speech-model download.
- `src/preload.cjs`: the `window.prompter` IPC bridge. It must stay CommonJS because the renderer is sandboxed.
- `src/settingsStore.js`: JSON settings stored at `<userData>/settings.json`.
- `src/renderer/app.js`: UI state and wiring, ported from `MainWindow.xaml.cs`. A single `render()` updates the DOM from `state.settings`. `setSetting()` runs side effects in `onSettingChanged()` and schedules a debounced save.
- `src/renderer/services/`: line-by-line ports of `Services/*.cs`. These are plain ES modules, so `npm test` can import the DOM-free ones directly.

## Decisions and why

- **Voice-follow uses Vosk (`vosk-browser`), not the Web Speech API.** Chromium's Web Speech API needs Google's cloud service and does not work in Electron. Vosk runs offline. On the first voice-follow start, main downloads `vosk-model-small-en-us-0.15.tar.gz` (~40 MB) from ccoreilly.github.io into `<userData>/models/`. `PROMPTER_VOSK_MODEL_URL` overrides the source.
- **Pages load from a custom `app://` protocol, not `file://`.** The Vosk Web Worker (a blob URL) fetches the model, so it needs a standard, secure, fetch-enabled origin. `getUserMedia` also needs a secure context.
- **The CSP includes `'unsafe-eval'`.** The Vosk Emscripten worker calls `eval`. Without it, the model never loads.
- **`createVoskModel()` wraps `new Vosk.Model`.** `Vosk.createModel` never settles when the worker reports an error, so the status hung on "Loading".
- **The scroll offset is tracked as a float in the `viewport` object (`app.js`).** `scrollTop` rounding would otherwise stall slow manual speeds (<1 px per frame).
- **Prompt padding is `min(200px, 45cqh)` on a size container.** A fixed 200px padding overflowed the stage when the window was short.
- **Minimal mode uses `contenteditable="plaintext-only"` on `#promptText`.** `onPromptEdited()` flattens any `<br>`/`<div>` that Chromium inserts back into a single text node and keeps the caret. Voice-follow centering relies on there being one text node.
- **`hideFromCapture` (`setContentProtection`) defaults to on.** It works on Windows 10 2004+ and macOS and has no effect on Linux.
- **Transparent frameless windows are `resizable: false` on Windows.** The in-app grip (bottom right) resizes them through IPC.
- **The Linux-only `blur` handler re-raises the window.** COSMIC ignores X11 `_NET_WM_STATE_ABOVE`. This workaround is unconfirmed on Linux and not needed on Windows.

## Status

Verified on Arch Linux (COSMIC, Wayland session, app running under XWayland):

- 8/8 unit tests pass.
- An end-to-end test drove the real UI over the Chrome DevTools Protocol (`--remote-debugging-port`). It checked:
  - manual and timed scrolling;
  - the faster/slower controls;
  - the click-through toggle state;
  - mirror and rephrase;
  - voice-follow with a fake microphone (`--use-fake-device-for-media-stream --use-file-for-fake-audio-capture=<wav>`, using vosk-api's `python/example/test.wav`);
  - minimal mode: layout, typing, Enter, paste, play/pause, sliders, the start-over button, the 320×200 minimum, and exit.
- The packaged asar build serves correctly over `app://`.

Platform limitations found on Linux/COSMIC (all compositor-side; the app has no fix):

- **Click-through:** Electron sets the X11 input shape to 1×1 correctly (checked with `XShapeGetRectangles`), but COSMIC still delivers clicks to the overlay.
- **Always-on-top:** lost when another window is clicked.
- **Global hotkeys under XWayland:** fire only while an X11 window has focus.
- **Tiling:** COSMIC auto-tiles the window. `Super+G` floats it.

## Next: Windows test checklist

None of the following has been run on Windows yet:

1. `npm install && npm start` launches. The glass panel is transparent and has no frame.
2. Click-through (button and `Ctrl+Shift+F8`): clicks pass to apps behind, and `Ctrl+Shift+F8` turns it back off.
3. Always-on-top survives clicking other windows, including Chrome and fullscreen apps.
4. All hotkeys work: `Ctrl+Shift+F8`–`F12`. F12 must not open DevTools.
5. Hide from screen sharing: the overlay is invisible in Teams/Zoom/OBS/Snipping Tool captures but still visible locally.
6. Voice-follow: Windows asks for microphone permission, the model downloads, and the script recenters while you read.
7. The resize grip and window sliders work. Edge-resize is disabled on Windows by design.
8. Minimal mode: editing, play/pause, sliders, drag handle, start over, exit.
9. Bahnschrift renders. It is the UI font on Windows; Linux falls back to other fonts.
10. `npm run dist:win`: the portable exe runs, and settings persist in `%APPDATA%\Transparent Prompter Overlay`.

## Gotchas

- Do not kill test instances with `pkill -f 'electron.*ozone-platform=x11'`. The pattern matches the invoking shell and kills it. Use `pkill -f '[d]ist/electron --ozone-platform=x11'`.
- Test runs write to the real settings file. Back it up first or delete it afterwards.
- The original WPF settings (`%LocalAppData%\TransparentPrompterOverlay\settings.json`) are not imported.
