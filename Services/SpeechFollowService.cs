using System.Globalization;
using System.Speech.Recognition;
using System.Timers;
using TransparentPrompterOverlay.Models;
using Timer = System.Timers.Timer;

namespace TransparentPrompterOverlay.Services;

public sealed class SpeechFollowService : IDisposable
{
    private readonly ScriptAlignmentService _alignmentService;
    private readonly Timer _silenceTimer;
    private SpeechRecognitionEngine? _recognizer;
    private int _currentCharacterIndex;
    private DateTime _lastSpeechUtc;
    private bool _reportedSpeaking;

    public SpeechFollowService(ScriptAlignmentService alignmentService)
    {
        _alignmentService = alignmentService;
        _silenceTimer = new Timer(350);
        _silenceTimer.Elapsed += SilenceTimer_OnElapsed;
    }

    public event Action<SpeechFollowUpdate>? FollowUpdateReceived;
    public event Action<string>? StatusChanged;

    public bool IsListening { get; private set; }

    public string AvailabilityText { get; private set; } = "Voice-follow is ready when a supported speech recognizer is available.";

    public void UpdateScript(string script)
    {
        _alignmentService.UpdateScript(script);
    }

    public Task StartAsync(int currentCharacterIndex)
    {
        _currentCharacterIndex = currentCharacterIndex;
        EnsureRecognizer();
        if (_recognizer is null)
        {
            return Task.CompletedTask;
        }

        if (IsListening)
        {
            return Task.CompletedTask;
        }

        try
        {
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            _silenceTimer.Start();
            _lastSpeechUtc = DateTime.UtcNow;
            IsListening = true;
            StatusChanged?.Invoke("Voice-follow is listening. Read naturally and the overlay will try to keep your current line centered.");
        }
        catch (InvalidOperationException exception)
        {
            AvailabilityText = $"Voice-follow could not start: {exception.Message}";
            StatusChanged?.Invoke(AvailabilityText);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsListening)
        {
            return Task.CompletedTask;
        }

        try
        {
            _recognizer?.RecognizeAsyncCancel();
            _recognizer?.RecognizeAsyncStop();
        }
        catch
        {
            // Best effort stop.
        }

        _silenceTimer.Stop();
        IsListening = false;
        _reportedSpeaking = false;
        FollowUpdateReceived?.Invoke(new SpeechFollowUpdate(false, string.Empty, null));
        StatusChanged?.Invoke("Voice-follow is paused.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _silenceTimer.Stop();
        _silenceTimer.Dispose();

        if (_recognizer is not null)
        {
            _recognizer.SpeechRecognized -= Recognizer_OnSpeechRecognized;
            _recognizer.SpeechHypothesized -= Recognizer_OnSpeechHypothesized;
            _recognizer.RecognizeCompleted -= Recognizer_OnRecognizeCompleted;
            _recognizer.Dispose();
            _recognizer = null;
        }
    }

    private void EnsureRecognizer()
    {
        if (_recognizer is not null)
        {
            return;
        }

        try
        {
            var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(recognizer => recognizer.Culture.Equals(CultureInfo.CurrentUICulture))
                ?? SpeechRecognitionEngine.InstalledRecognizers()
                    .FirstOrDefault(recognizer => recognizer.Culture.TwoLetterISOLanguageName == "en")
                ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();

            if (recognizerInfo is null)
            {
                AvailabilityText = "Voice-follow needs a Windows speech recognizer. Install a speech language pack in Windows settings.";
                StatusChanged?.Invoke(AvailabilityText);
                return;
            }

            _recognizer = new SpeechRecognitionEngine(recognizerInfo);
            _recognizer.SetInputToDefaultAudioDevice();
            _recognizer.LoadGrammar(new DictationGrammar());
            _recognizer.SpeechRecognized += Recognizer_OnSpeechRecognized;
            _recognizer.SpeechHypothesized += Recognizer_OnSpeechHypothesized;
            _recognizer.RecognizeCompleted += Recognizer_OnRecognizeCompleted;
            AvailabilityText = $"Voice-follow is using {recognizerInfo.Culture.DisplayName}.";
        }
        catch (Exception exception)
        {
            AvailabilityText = $"Voice-follow is unavailable: {exception.Message}";
            StatusChanged?.Invoke(AvailabilityText);
            _recognizer = null;
        }
    }

    private void Recognizer_OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result?.Text is not { Length: > 0 } text || e.Result.Confidence < 0.35)
        {
            return;
        }

        PublishTranscript(text);
    }

    private void Recognizer_OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        if (e.Result?.Text is not { Length: > 0 } text)
        {
            return;
        }

        PublishTranscript(text);
    }

    private void Recognizer_OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (IsListening && e.Error is not null)
        {
            StatusChanged?.Invoke($"Voice-follow stopped: {e.Error.Message}");
        }
    }

    private void PublishTranscript(string text)
    {
        _lastSpeechUtc = DateTime.UtcNow;
        _reportedSpeaking = true;

        var alignment = _alignmentService.TryAlign(text, _currentCharacterIndex);
        if (alignment is not null)
        {
            _currentCharacterIndex = alignment.CharacterIndex;
        }

        FollowUpdateReceived?.Invoke(new SpeechFollowUpdate(true, text, alignment));
    }

    private void SilenceTimer_OnElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsListening || !_reportedSpeaking)
        {
            return;
        }

        if (DateTime.UtcNow - _lastSpeechUtc < TimeSpan.FromSeconds(1.2))
        {
            return;
        }

        _reportedSpeaking = false;
        FollowUpdateReceived?.Invoke(new SpeechFollowUpdate(false, string.Empty, null));
    }
}
