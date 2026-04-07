# Transparent Prompter Overlay

A lightweight Windows teleprompter overlay built with WPF.

## Why WPF

WPF is the best fit here because it gives reliable Windows-native support for:

- borderless always-on-top windows
- transparent layered visuals
- Win32 click-through behavior
- global hotkeys
- responsive text rendering for large scripts

That lets the app feel like a true prompter overlay instead of a repurposed notes app.

## Features

- Borderless, always-on-top floating overlay
- Semi-transparent glass-style background with readable prompt text
- Drag-to-move header and resize grip
- Adjustable opacity, font size, text color, and text alignment
- Optional mirrored prompt text for reflective teleprompter glass
- Editable script panel with live prompt preview
- Manual scrolling mode with start/pause and speed controls
- Timed scrolling mode with editable target duration
- Voice-follow mode using Windows speech recognition
- Rephrase assistant with 2-3 simpler alternatives for selected text
- Click-through mode so mouse input can pass to apps behind the overlay
- Global hotkeys:
  - `Ctrl+Shift+F8` toggle click-through
  - `Ctrl+Shift+F9` show or hide the overlay
  - `Ctrl+Shift+F10` start or pause scrolling / voice-follow
  - `Ctrl+Shift+F11` speed up manual mode or shorten timed mode
  - `Ctrl+Shift+F12` slow down manual mode or lengthen timed mode
- Local persistence for the script and settings at `%LocalAppData%\TransparentPrompterOverlay\settings.json`

## Run It

From the project folder:

```powershell
dotnet restore
dotnet build
dotnet run --project TransparentPrompterOverlay.csproj
```

## How It Works

### Transparency

- The window uses `AllowsTransparency="True"` and `WindowStyle="None"` so the real app window chrome disappears.
- The visible glass panel is drawn inside that transparent window with a configurable alpha value.
- Prompt text stays opaque while the panel stays see-through.

### Click-through

- The app toggles the Win32 extended style `WS_EX_TRANSPARENT`.
- When click-through is on, mouse input passes to the app behind the overlay.
- When click-through is off, the window becomes editable, draggable, and resizable again.

### Timed scrolling

- The timed scroll service captures the current scroll position and the total remaining distance.
- It then advances the prompt toward the bottom over the requested duration using a lightweight UI timer.
- Adjusting the timed duration while running re-paces the remaining script.

### Voice-follow

- Voice-follow uses `System.Speech` with dictation recognition on Windows.
- Recognized phrases are matched back to the script using a practical alignment service that searches for recent word sequences and approximates spoken progress.
- When a match is found, the app recenters the matching part of the script in the prompt stage.
- If speech stops, the prompt stops moving until speech resumes.

### Voice-follow limitations

- It works best when the spoken wording stays fairly close to the written script.
- Recognition quality depends on microphone quality, room noise, and installed Windows speech language support.
- It is intentionally practical rather than perfect: it aims to keep the current line centered smoothly, not to perform exact word-by-word tracking.

### AI rephrase architecture

- The rephrase feature is behind `ILineRephraseService`.
- The current implementation is a local heuristic provider that produces simpler wording, shorter delivery, and more conversational alternatives.
- A future API provider can replace or extend that service without changing the overlay UI.

## Project Layout

- `MainWindow.xaml` and `MainWindow.xaml.cs`: overlay shell, UI wiring, hotkeys, click-through, and viewport behavior
- `Models/OverlaySettings.cs`: persisted script and overlay settings
- `Models/TeleprompterMode.cs`: scroll mode selection
- `Models/TextColorOption.cs` and `Models/TextAlignmentOption.cs`: visual control presets
- `Models/RephraseSuggestion.cs`, `Models/ScriptSelection.cs`, `Models/SpeechAlignmentResult.cs`, `Models/SpeechFollowUpdate.cs`: feature-specific data models
- `Native/NativeMethods.cs`: Win32 interop for hotkeys and window styles
- `Services/TeleprompterScrollService.cs`: manual and timed scrolling engine
- `Services/SpeechFollowService.cs`: speech recognition lifecycle and follow updates
- `Services/ScriptAlignmentService.cs`: practical script matching for spoken fragments
- `Services/ILineRephraseService.cs` and `Services/LocalHeuristicRephraseService.cs`: pluggable rephrase feature
- `Services/SettingsStore.cs`: local JSON persistence
- `Services/GlobalHotkeyManager.cs`: global hotkey registration
