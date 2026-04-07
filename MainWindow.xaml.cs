using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TransparentPrompterOverlay.Models;
using TransparentPrompterOverlay.Native;
using TransparentPrompterOverlay.Services;

namespace TransparentPrompterOverlay;

public partial class MainWindow : Window, INotifyPropertyChanged, IScrollViewport
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TeleprompterScrollService _scrollService = new();
    private readonly SpeechFollowService _speechFollowService;
    private readonly ILineRephraseService _rephraseService = new LocalHeuristicRephraseService();
    private readonly DispatcherTimer _saveTimer;
    private readonly List<RephraseSuggestion> _rephraseSuggestions = [];
    private GlobalHotkeyManager? _hotkeyManager;
    private ScrollViewer? _promptScrollViewer;
    private CancellationTokenSource? _rephraseCts;
    private ScriptSelection? _lastRephraseSelection;
    private bool _isSyncingWindowBounds;
    private Brush _overlayBackgroundBrush = Brushes.Transparent;
    private Brush _textBrush = Brushes.White;
    private string _timedDurationText = string.Empty;
    private string _timedDurationHelperText = "Use mm:ss or a number of seconds.";
    private string _rephraseStatusText = "No suggestions yet.";
    private string _voiceStatusText = "Voice-follow works best when you read the saved script clearly and stay fairly close to the original wording.";
    private int _currentPromptCharacterIndex;

    public MainWindow()
    {
        _speechFollowService = new SpeechFollowService(new ScriptAlignmentService());
        Settings = _settingsStore.Load();
        Settings.ClickThroughEnabled = false;

        TextColorOptions = TextColorOption.CreateDefaults();
        TextAlignmentOptions = TextAlignmentOption.CreateDefaults();
        TeleprompterModeOptions = TeleprompterModeOption.CreateDefaults();

        NormalizeSettings();
        _timedDurationText = FormatDuration(Settings.TimedDurationSeconds);

        InitializeComponent();
        DataContext = this;

        ApplyStoredWindowBounds();
        RefreshVisualState();

        _scrollService.ManualSpeedPixelsPerSecond = Settings.ManualScrollSpeed;
        _scrollService.TimedDuration = TimeSpan.FromSeconds(Settings.TimedDurationSeconds);
        _scrollService.PlaybackStateChanged += ScrollService_OnPlaybackStateChanged;

        _speechFollowService.UpdateScript(Settings.ScriptText);
        _speechFollowService.FollowUpdateReceived += SpeechFollowService_OnFollowUpdateReceived;
        _speechFollowService.StatusChanged += SpeechFollowService_OnStatusChanged;

        Settings.PropertyChanged += Settings_OnPropertyChanged;
        LocationChanged += MainWindow_OnLocationChanged;
        SizeChanged += MainWindow_OnSizeChanged;
        Loaded += MainWindow_OnLoaded;
        SourceInitialized += MainWindow_OnSourceInitialized;
        Closing += MainWindow_OnClosing;

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _saveTimer.Tick += SaveTimer_OnTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlaySettings Settings { get; }

    public IReadOnlyList<TextColorOption> TextColorOptions { get; }

    public IReadOnlyList<TextAlignmentOption> TextAlignmentOptions { get; }

    public IReadOnlyList<TeleprompterModeOption> TeleprompterModeOptions { get; }

    public Brush OverlayBackgroundBrush
    {
        get => _overlayBackgroundBrush;
        private set
        {
            _overlayBackgroundBrush = value;
            OnPropertyChanged(nameof(OverlayBackgroundBrush));
        }
    }

    public Brush TextBrush
    {
        get => _textBrush;
        private set
        {
            _textBrush = value;
            OnPropertyChanged(nameof(TextBrush));
        }
    }

    public string PrimaryActionButtonLabel =>
        Settings.ClickThroughEnabled ? "Edit / unlock" : "Enable click-through";

    public string ModeBadgeText =>
        $"{GetModeLabel(Settings.TeleprompterMode)} • {(Settings.ClickThroughEnabled ? "click-through" : "editable")}";

    public string PlaybackBadgeText =>
        IsPlaybackActive()
            ? "playback active"
            : $"{GetModeLabel(Settings.TeleprompterMode).ToLowerInvariant()} ready";

    public string PlaybackButtonLabel =>
        Settings.TeleprompterMode == TeleprompterMode.VoiceFollow
            ? (_speechFollowService.IsListening ? "Pause voice-follow" : "Start voice-follow")
            : (_scrollService.IsRunning ? "Pause scrolling" : "Start scrolling");

    public string PlaybackStatusText =>
        Settings.TeleprompterMode switch
        {
            TeleprompterMode.Manual when _scrollService.IsRunning => $"Manual mode is scrolling at {Settings.ManualScrollSpeed:0} px/s.",
            TeleprompterMode.Manual => "Manual mode waits for Start and moves at the speed you choose.",
            TeleprompterMode.Timed when _scrollService.IsRunning => $"Timed mode is pacing to finish in {FormatDuration(Settings.TimedDurationSeconds)}.",
            TeleprompterMode.Timed => $"Timed mode will finish in {FormatDuration(Settings.TimedDurationSeconds)} once you press Start.",
            TeleprompterMode.VoiceFollow when _speechFollowService.IsListening => "Voice-follow is listening and will recenter the current line when speech matches the script.",
            _ => "Voice-follow is paused. Press Start to listen for your place in the script."
        };

    public string VoiceStatusText => _voiceStatusText;

    public string ManualSpeedDisplayText => $"{Settings.ManualScrollSpeed:0} px/s";

    public string TimedDurationText
    {
        get => _timedDurationText;
        set
        {
            if (_timedDurationText == value)
            {
                return;
            }

            _timedDurationText = value;
            if (TryParseDuration(value, out var totalSeconds))
            {
                Settings.TimedDurationSeconds = totalSeconds;
                _timedDurationHelperText = $"Timed mode will finish in {FormatDuration(totalSeconds)}.";
            }
            else
            {
                _timedDurationHelperText = "Use mm:ss, like 03:30, or enter total seconds.";
            }

            OnPropertyChanged(nameof(TimedDurationText));
            OnPropertyChanged(nameof(TimedDurationHelperText));
        }
    }

    public string TimedDurationHelperText => _timedDurationHelperText;

    public string RephraseStatusText => _rephraseStatusText;

    public IReadOnlyList<RephraseSuggestion> RephraseSuggestions => _rephraseSuggestions;

    public Visibility RephraseSuggestionsVisibility =>
        _rephraseSuggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EditorVisibility =>
        Settings.ClickThroughEnabled ? Visibility.Collapsed : Visibility.Visible;

    public Visibility EmptyEditorHintVisibility =>
        string.IsNullOrWhiteSpace(Settings.ScriptText) ? Visibility.Visible : Visibility.Collapsed;

    public double PromptScaleX => Settings.MirrorText ? -1 : 1;

    public string StatusLineText
    {
        get
        {
            if (Settings.ClickThroughEnabled)
            {
                return "Click-through is on. Press Ctrl+Shift+F8 to edit, move, or rephrase the script again.";
            }

            return Settings.TeleprompterMode switch
            {
                TeleprompterMode.Manual => "Manual mode keeps the stage glanceable while you control the pace directly.",
                TeleprompterMode.Timed => "Timed mode spreads the remaining script across your target duration and is best for a known run length.",
                _ => "Voice-follow is practical rather than perfect: it works best when you stay fairly close to the written script and use a clear mic."
            };
        }
    }

    public double CurrentOffset => _promptScrollViewer?.VerticalOffset ?? 0;

    public double MaxOffset => _promptScrollViewer?.ScrollableHeight ?? 0;

    public void ScrollToOffset(double offset)
    {
        if (_promptScrollViewer is null)
        {
            return;
        }

        var clamped = Math.Clamp(offset, 0, _promptScrollViewer.ScrollableHeight);
        _promptScrollViewer.ScrollToVerticalOffset(clamped);
        _currentPromptCharacterIndex = EstimateCharacterIndexFromOffset(clamped);
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _promptScrollViewer = VisualTreeUtilities.FindDescendant<ScrollViewer>(PromptDisplayTextBox);
            _scrollService.AttachViewport(this);
            FocusEditor();
        }, DispatcherPriority.Loaded);
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        _hotkeyManager = new GlobalHotkeyManager(windowHandle);
        _hotkeyManager.HotkeyPressed += HandleHotkeyPressed;

        try
        {
            _hotkeyManager.RegisterDefaultHotkeys();
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                this,
                $"{exception.Message}\r\n\r\nThe overlay will still work, but one or more hotkeys are already taken by another app.",
                "Hotkey Registration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ApplyClickThroughState();
    }

    private async void HandleHotkeyPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleClickThrough:
                ToggleClickThrough();
                break;
            case HotkeyAction.ToggleVisibility:
                ToggleOverlayVisibility();
                break;
            case HotkeyAction.TogglePlayback:
                await TogglePlaybackAsync();
                break;
            case HotkeyAction.IncreaseScrollRate:
                AdjustPlaybackRate(faster: true);
                break;
            case HotkeyAction.DecreaseScrollRate:
                AdjustPlaybackRate(faster: false);
                break;
        }
    }

    private void DragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Settings.ClickThroughEnabled || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void ToggleClickThroughButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleClickThrough();
    }

    private async void PlaybackButton_OnClick(object sender, RoutedEventArgs e)
    {
        await TogglePlaybackAsync();
    }

    private void RestartFromTopButton_OnClick(object sender, RoutedEventArgs e)
    {
        _scrollService.RestartFromTop();
        _currentPromptCharacterIndex = 0;
    }

    private void FasterButton_OnClick(object sender, RoutedEventArgs e)
    {
        AdjustPlaybackRate(faster: true);
    }

    private void SlowerButton_OnClick(object sender, RoutedEventArgs e)
    {
        AdjustPlaybackRate(faster: false);
    }

    private async void RephraseSelectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        _rephraseCts?.Cancel();
        _rephraseCts = new CancellationTokenSource();
        _lastRephraseSelection = ScriptSelectionService.GetSelectionOrNearbyParagraph(
            Settings.ScriptText,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength);

        if (string.IsNullOrWhiteSpace(_lastRephraseSelection.Text))
        {
            ReplaceRephraseSuggestions([]);
            _rephraseStatusText = "Select text or place the caret inside a non-empty paragraph first.";
            NotifyRephraseUiChanged();
            return;
        }

        _rephraseStatusText = $"Rephrasing the {_lastRephraseSelection.Label.ToLowerInvariant()}...";
        NotifyRephraseUiChanged();

        try
        {
            var suggestions = await _rephraseService.RephraseAsync(_lastRephraseSelection.Text, _rephraseCts.Token);
            ReplaceRephraseSuggestions(suggestions);
            _rephraseStatusText = _rephraseSuggestions.Count == 0
                ? "No alternatives were generated for that text."
                : $"Showing {_rephraseSuggestions.Count} easier-to-say alternatives for the {_lastRephraseSelection.Label.ToLowerInvariant()}.";
        }
        catch (OperationCanceledException)
        {
            return;
        }

        NotifyRephraseUiChanged();
    }

    private void ApplySuggestionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RephraseSuggestion suggestion } ||
            _lastRephraseSelection is null ||
            _lastRephraseSelection.Length <= 0)
        {
            return;
        }

        var selection = _lastRephraseSelection;
        Settings.ScriptText = Settings.ScriptText.Remove(selection.Start, selection.Length)
            .Insert(selection.Start, suggestion.Text);

        EditorTextBox.Focus();
        EditorTextBox.Select(selection.Start, suggestion.Text.Length);
        _lastRephraseSelection = selection with { Length = suggestion.Text.Length, Text = suggestion.Text };
        _rephraseStatusText = $"{suggestion.Title} applied to the script.";
        NotifyRephraseUiChanged();
    }

    private void HideOverlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleOverlayVisibility();
    }

    private void ResetViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        Settings.OverlayOpacity = 0.28;
        Settings.FontSize = 34;
        Settings.TextColorHex = "#F8FAFC";
        Settings.TextAlignment = TextAlignment.Center;
        Settings.MirrorText = false;
        Settings.WindowWidth = 1120;
        Settings.WindowHeight = 780;
        Settings.ManualScrollSpeed = 54;
        Settings.TimedDurationSeconds = 180;
        Settings.TeleprompterMode = TeleprompterMode.Manual;
        Settings.ClickThroughEnabled = false;
        _timedDurationHelperText = "Timed mode will finish in 03:00.";
        ScrollToOffset(0);
        NotifyPlaybackUiChanged();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var maxWidth = Math.Max(MinWidth, SystemParameters.VirtualScreenWidth - 40);
        var maxHeight = Math.Max(MinHeight, SystemParameters.VirtualScreenHeight - 40);

        Settings.WindowWidth = Math.Clamp(Width + e.HorizontalChange, MinWidth, maxWidth);
        Settings.WindowHeight = Math.Clamp(Height + e.VerticalChange, MinHeight, maxHeight);
    }

    private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverlaySettings.OverlayOpacity):
            case nameof(OverlaySettings.TextColorHex):
                RefreshVisualState();
                break;
            case nameof(OverlaySettings.WindowWidth):
                SyncWindowDimension(nameof(Width), Settings.WindowWidth);
                break;
            case nameof(OverlaySettings.WindowHeight):
                SyncWindowDimension(nameof(Height), Settings.WindowHeight);
                break;
            case nameof(OverlaySettings.ScriptText):
                _speechFollowService.UpdateScript(Settings.ScriptText);
                OnPropertyChanged(nameof(EmptyEditorHintVisibility));
                break;
            case nameof(OverlaySettings.ClickThroughEnabled):
                ApplyClickThroughState();
                OnPropertyChanged(nameof(EditorVisibility));
                OnPropertyChanged(nameof(PrimaryActionButtonLabel));
                OnPropertyChanged(nameof(ModeBadgeText));
                OnPropertyChanged(nameof(StatusLineText));
                if (!Settings.ClickThroughEnabled)
                {
                    FocusEditor();
                }

                break;
            case nameof(OverlaySettings.ManualScrollSpeed):
                _scrollService.ManualSpeedPixelsPerSecond = Settings.ManualScrollSpeed;
                OnPropertyChanged(nameof(ManualSpeedDisplayText));
                break;
            case nameof(OverlaySettings.TimedDurationSeconds):
                _scrollService.TimedDuration = TimeSpan.FromSeconds(Settings.TimedDurationSeconds);
                _timedDurationText = FormatDuration(Settings.TimedDurationSeconds);
                OnPropertyChanged(nameof(TimedDurationText));
                OnPropertyChanged(nameof(TimedDurationHelperText));
                break;
            case nameof(OverlaySettings.MirrorText):
                OnPropertyChanged(nameof(PromptScaleX));
                break;
            case nameof(OverlaySettings.TeleprompterMode):
                PauseAllPlayback();
                NotifyPlaybackUiChanged();
                break;
        }

        ScheduleSave();
    }

    private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isSyncingWindowBounds)
        {
            return;
        }

        Settings.WindowLeft = Left;
        Settings.WindowTop = Top;
    }

    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isSyncingWindowBounds)
        {
            return;
        }

        Settings.WindowWidth = Width;
        Settings.WindowHeight = Height;
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        Settings.WindowLeft = Left;
        Settings.WindowTop = Top;
        SaveSettings();
        _saveTimer.Stop();
        _scrollService.Dispose();
        await _speechFollowService.StopAsync();
        _speechFollowService.Dispose();
        _rephraseCts?.Cancel();
        _hotkeyManager?.Dispose();
    }

    private void SaveTimer_OnTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveSettings();
    }

    private void ScrollService_OnPlaybackStateChanged()
    {
        NotifyPlaybackUiChanged();
    }

    private void SpeechFollowService_OnStatusChanged(string status)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _voiceStatusText = status;
            NotifyPlaybackUiChanged();
        }, DispatcherPriority.Background);
    }

    private void SpeechFollowService_OnFollowUpdateReceived(SpeechFollowUpdate update)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (update.IsSpeaking)
            {
                _voiceStatusText = update.Alignment is not null
                    ? $"Hearing: “{update.TranscriptFragment}” and matching it inside the script."
                    : $"Hearing: “{update.TranscriptFragment}”. Waiting for a stronger script match.";
            }
            else
            {
                _voiceStatusText = "Voice-follow is waiting for you to continue speaking.";
            }

            if (Settings.TeleprompterMode == TeleprompterMode.VoiceFollow &&
                update.Alignment is not null)
            {
                CenterPromptOnCharacter(update.Alignment.CharacterIndex);
            }

            NotifyPlaybackUiChanged();
        }, DispatcherPriority.Background);
    }

    private void ToggleClickThrough()
    {
        Settings.ClickThroughEnabled = !Settings.ClickThroughEnabled;
    }

    private void ToggleOverlayVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
        Topmost = true;

        if (!Settings.ClickThroughEnabled)
        {
            FocusEditor();
        }
    }

    private async Task TogglePlaybackAsync()
    {
        switch (Settings.TeleprompterMode)
        {
            case TeleprompterMode.Manual:
            case TeleprompterMode.Timed:
                await _speechFollowService.StopAsync();
                _scrollService.ManualSpeedPixelsPerSecond = Settings.ManualScrollSpeed;
                _scrollService.TimedDuration = TimeSpan.FromSeconds(Settings.TimedDurationSeconds);
                _scrollService.Toggle(Settings.TeleprompterMode);
                break;
            case TeleprompterMode.VoiceFollow:
                _scrollService.Pause();
                if (_speechFollowService.IsListening)
                {
                    await _speechFollowService.StopAsync();
                }
                else
                {
                    await _speechFollowService.StartAsync(GetApproximateCurrentCharacterIndex());
                }

                break;
        }

        NotifyPlaybackUiChanged();
    }

    private void AdjustPlaybackRate(bool faster)
    {
        switch (Settings.TeleprompterMode)
        {
            case TeleprompterMode.Manual:
                Settings.ManualScrollSpeed = Math.Clamp(Settings.ManualScrollSpeed + (faster ? 10 : -10), 10, 220);
                break;
            case TeleprompterMode.Timed:
                Settings.TimedDurationSeconds = Math.Clamp(Settings.TimedDurationSeconds + (faster ? -15 : 15), 30, 5400);
                _timedDurationHelperText = $"Timed mode is set to {FormatDuration(Settings.TimedDurationSeconds)}.";
                if (_scrollService.IsRunning)
                {
                    _scrollService.Start(TeleprompterMode.Timed);
                }

                NotifyPlaybackUiChanged();
                break;
            case TeleprompterMode.VoiceFollow:
                _voiceStatusText = "Voice-follow tracks your speech rather than a manual speed setting.";
                NotifyPlaybackUiChanged();
                break;
        }
    }

    private void PauseAllPlayback()
    {
        _scrollService.Pause();
        _speechFollowService.StopAsync();
    }

    private void RefreshVisualState()
    {
        var overlayColor = Color.FromArgb(
            (byte)Math.Round(Settings.OverlayOpacity * 255),
            8,
            14,
            24);

        OverlayBackgroundBrush = new SolidColorBrush(overlayColor);
        TextBrush = CreateBrushFromHex(Settings.TextColorHex, Colors.White);
    }

    private void ApplyStoredWindowBounds()
    {
        _isSyncingWindowBounds = true;

        Width = Settings.WindowWidth;
        Height = Settings.WindowHeight;

        var virtualBounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (Settings.WindowLeft is double left &&
            Settings.WindowTop is double top)
        {
            var storedBounds = new Rect(left, top, Settings.WindowWidth, Settings.WindowHeight);
            if (storedBounds.IntersectsWith(virtualBounds))
            {
                Left = left;
                Top = top;
            }
        }

        _isSyncingWindowBounds = false;
    }

    private void SyncWindowDimension(string dimensionName, double settingValue)
    {
        _isSyncingWindowBounds = true;

        if (dimensionName == nameof(Width) && Math.Abs(Width - settingValue) > 0.5)
        {
            Width = settingValue;
        }
        else if (dimensionName == nameof(Height) && Math.Abs(Height - settingValue) > 0.5)
        {
            Height = settingValue;
        }

        _isSyncingWindowBounds = false;
    }

    private void ApplyClickThroughState()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var currentStyles = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
        currentStyles |= NativeMethods.WS_EX_LAYERED;

        if (Settings.ClickThroughEnabled)
        {
            currentStyles |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            currentStyles &= ~NativeMethods.WS_EX_TRANSPARENT;
        }

        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(currentStyles));
        NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_FRAMECHANGED);
    }

    private void NormalizeSettings()
    {
        Settings.OverlayOpacity = Math.Clamp(Settings.OverlayOpacity, 0.12, 0.78);
        Settings.FontSize = Math.Clamp(Settings.FontSize, 20, 76);
        Settings.WindowWidth = Math.Clamp(Settings.WindowWidth, 760, 1600);
        Settings.WindowHeight = Math.Clamp(Settings.WindowHeight, 520, 980);
        Settings.ManualScrollSpeed = Math.Clamp(Settings.ManualScrollSpeed, 10, 220);
        Settings.TimedDurationSeconds = Math.Clamp(Settings.TimedDurationSeconds, 30, 5400);

        if (string.IsNullOrWhiteSpace(Settings.ScriptText))
        {
            Settings.ScriptText = "Paste or type your script here.";
        }

        if (!TryConvertHexToColor(Settings.TextColorHex, out _))
        {
            Settings.TextColorHex = "#F8FAFC";
        }
    }

    private void SaveSettings()
    {
        _settingsStore.Save(Settings);
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FocusEditor()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void CenterPromptOnCharacter(int characterIndex)
    {
        if (_promptScrollViewer is null || Settings.ScriptText.Length == 0)
        {
            return;
        }

        characterIndex = Math.Clamp(characterIndex, 0, Math.Max(0, Settings.ScriptText.Length - 1));
        var characterRect = PromptDisplayTextBox.GetRectFromCharacterIndex(characterIndex, true);

        double targetOffset;
        if (characterRect.IsEmpty || double.IsNaN(characterRect.Top))
        {
            targetOffset = MaxOffset * (characterIndex / (double)Math.Max(1, Settings.ScriptText.Length - 1));
        }
        else
        {
            targetOffset = CurrentOffset + characterRect.Top - (_promptScrollViewer.ViewportHeight / 2) + (characterRect.Height / 2);
        }

        ScrollToOffset(targetOffset);
        _currentPromptCharacterIndex = characterIndex;
    }

    private int GetApproximateCurrentCharacterIndex()
    {
        if (Settings.ScriptText.Length == 0)
        {
            return 0;
        }

        if (MaxOffset <= 0)
        {
            return _currentPromptCharacterIndex;
        }

        var progress = CurrentOffset / MaxOffset;
        return Math.Clamp((int)Math.Round(progress * (Settings.ScriptText.Length - 1)), 0, Settings.ScriptText.Length - 1);
    }

    private int EstimateCharacterIndexFromOffset(double offset)
    {
        if (Settings.ScriptText.Length == 0 || MaxOffset <= 0)
        {
            return 0;
        }

        var progress = offset / MaxOffset;
        return Math.Clamp((int)Math.Round(progress * (Settings.ScriptText.Length - 1)), 0, Settings.ScriptText.Length - 1);
    }

    private static string GetModeLabel(TeleprompterMode mode) =>
        mode switch
        {
            TeleprompterMode.Manual => "Manual",
            TeleprompterMode.Timed => "Timed",
            _ => "Voice-follow"
        };

    private static bool TryParseDuration(string input, out int totalSeconds)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            totalSeconds = 0;
            return false;
        }

        if (input.Contains(':'))
        {
            var parts = input.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var minutes) &&
                int.TryParse(parts[1], out var seconds) &&
                seconds is >= 0 and < 60)
            {
                totalSeconds = (minutes * 60) + seconds;
                return totalSeconds >= 30;
            }
        }

        if (int.TryParse(input, out totalSeconds))
        {
            return totalSeconds >= 30;
        }

        totalSeconds = 0;
        return false;
    }

    private static string FormatDuration(int totalSeconds)
    {
        var safeDuration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return $"{(int)safeDuration.TotalMinutes:00}:{safeDuration.Seconds:00}";
    }

    private static Brush CreateBrushFromHex(string hexValue, Color fallbackColor)
    {
        return TryConvertHexToColor(hexValue, out var color)
            ? new SolidColorBrush(color)
            : new SolidColorBrush(fallbackColor);
    }

    private static bool TryConvertHexToColor(string hexValue, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hexValue);
            return true;
        }
        catch
        {
            color = Colors.White;
            return false;
        }
    }

    private bool IsPlaybackActive() => _scrollService.IsRunning || _speechFollowService.IsListening;

    private void ReplaceRephraseSuggestions(IEnumerable<RephraseSuggestion> suggestions)
    {
        _rephraseSuggestions.Clear();
        _rephraseSuggestions.AddRange(suggestions);
    }

    private void NotifyPlaybackUiChanged()
    {
        OnPropertyChanged(nameof(PlaybackButtonLabel));
        OnPropertyChanged(nameof(PlaybackStatusText));
        OnPropertyChanged(nameof(PlaybackBadgeText));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(VoiceStatusText));
        OnPropertyChanged(nameof(StatusLineText));
        OnPropertyChanged(nameof(TimedDurationHelperText));
    }

    private void NotifyRephraseUiChanged()
    {
        OnPropertyChanged(nameof(RephraseStatusText));
        OnPropertyChanged(nameof(RephraseSuggestions));
        OnPropertyChanged(nameof(RephraseSuggestionsVisibility));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
