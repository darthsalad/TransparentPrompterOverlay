using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Media.Devices;
using Windows.Media.SpeechRecognition;

// Protocol (one JSON object per line on stdout):
//   {"type":"ready","language":"en-US","microphone":"..."}
//   {"type":"state","state":"SoundStarted"}    recognizer state changes (Capturing, SoundStarted, SpeechDetected, ...)
//   {"type":"audio","problem":"TooQuiet"}      audio quality problems (NoSignal, TooQuiet, TooNoisy, ...)
//   {"type":"partial","text":"..."}      hypothesis while the user is still speaking
//   {"type":"result","text":"..."}       final text for a phrase
//   {"type":"error","code":"...","message":"..."}
// The helper stops when stdin closes or receives the line "stop".
// Optional argument: a BCP-47 language tag such as en-GB. The default is the Windows speech language.

const int HResultPrivacyStatementDeclined = unchecked((int)0x80045509);

var output = Console.Out;
var outputLock = new object();
var stopping = new TaskCompletionSource();

void Emit(object message)
{
    var line = JsonSerializer.Serialize(message);
    lock (outputLock)
    {
        output.WriteLine(line);
        output.Flush();
    }
}

void Fail(string code, string message)
{
    Emit(new { type = "error", code, message });
    stopping.TrySetResult();
}

_ = Task.Run(() =>
{
    string? line;
    while ((line = Console.In.ReadLine()) is not null)
    {
        if (line.Trim() == "stop")
        {
            break;
        }
    }

    stopping.TrySetResult();
});

SpeechRecognizer recognizer;
try
{
    recognizer = args.Length > 0 ? new SpeechRecognizer(new Language(args[0])) : new SpeechRecognizer();
}
catch (Exception error)
{
    Fail("unavailable", $"Windows speech recognition is not available: {error.Message}");
    return 1;
}

using (recognizer)
{
    recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
    recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(30);
    recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromMilliseconds(600);
    recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromDays(1);

    var compiled = await recognizer.CompileConstraintsAsync();
    if (compiled.Status != SpeechRecognitionResultStatus.Success)
    {
        Fail("compile", $"Windows speech recognition could not start ({compiled.Status}).");
        return 1;
    }

    recognizer.StateChanged += (_, e) => Emit(new { type = "state", state = e.State.ToString() });
    recognizer.RecognitionQualityDegrading += (_, e) => Emit(new { type = "audio", problem = e.Problem.ToString() });

    recognizer.HypothesisGenerated += (_, e) =>
    {
        if (!string.IsNullOrWhiteSpace(e.Hypothesis.Text))
        {
            Emit(new { type = "partial", text = e.Hypothesis.Text });
        }
    };

    recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
    {
        Emit(new { type = "debug", @event = "result", status = e.Result.Status.ToString(), confidence = e.Result.Confidence.ToString(), text = e.Result.Text });
        if (!string.IsNullOrWhiteSpace(e.Result.Text))
        {
            Emit(new { type = "result", text = e.Result.Text });
        }
    };

    // The session ends on its own after timeouts or network hiccups; restart it until asked to stop.
    recognizer.ContinuousRecognitionSession.Completed += async (_, e) =>
    {
        Emit(new { type = "debug", @event = "completed", status = e.Status.ToString() });
        if (stopping.Task.IsCompleted)
        {
            return;
        }

        if (e.Status is SpeechRecognitionResultStatus.MicrophoneUnavailable)
        {
            Fail("microphone", "No microphone is available, or Windows blocked microphone access for desktop apps.");
            return;
        }

        if (e.Status is SpeechRecognitionResultStatus.NetworkFailure)
        {
            Emit(new { type = "error", code = "network", message = "Windows speech recognition lost its network connection. Retrying..." });
            await Task.Delay(1000);
        }

        try
        {
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception error)
        {
            Fail("restart", $"Windows speech recognition stopped: {error.Message}");
        }
    };

    try
    {
        await recognizer.ContinuousRecognitionSession.StartAsync();
    }
    catch (Exception error) when (error.HResult == HResultPrivacyStatementDeclined)
    {
        Fail("privacy", "Turn on Online speech recognition in Windows Settings > Privacy & security > Speech, then start voice-follow again.");
        return 1;
    }
    catch (UnauthorizedAccessException)
    {
        Fail("microphone", "Windows blocked microphone access. Turn on \"Let desktop apps access your microphone\" in Settings > Privacy & security > Microphone.");
        return 1;
    }
    catch (COMException error)
    {
        Fail("start", $"Windows speech recognition could not start: {error.Message}");
        return 1;
    }

    Emit(new { type = "ready", language = recognizer.CurrentLanguage.LanguageTag, microphone = await DefaultMicrophoneName() });

    await stopping.Task;

    try
    {
        await recognizer.ContinuousRecognitionSession.StopAsync();
    }
    catch
    {
        // Already stopped.
    }
}

return 0;

static async Task<string> DefaultMicrophoneName()
{
    try
    {
        var id = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
        if (string.IsNullOrEmpty(id))
        {
            return "none";
        }

        return (await DeviceInformation.CreateFromIdAsync(id)).Name;
    }
    catch
    {
        return "unknown";
    }
}
