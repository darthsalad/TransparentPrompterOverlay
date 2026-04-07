using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace TransparentPrompterOverlay.Models;

public sealed class OverlaySettings : INotifyPropertyChanged
{
    private string _scriptText =
        "Paste or type your script here.\r\n\r\n" +
        "Manual mode scrolls at the speed you choose.\r\n" +
        "Timed mode finishes within the target duration.\r\n" +
        "Voice-follow mode listens for your place and recenters the current line.";
    private double _overlayOpacity = 0.28;
    private double _fontSize = 34;
    private string _textColorHex = "#F8FAFC";
    private TextAlignment _textAlignment = TextAlignment.Center;
    private bool _mirrorText;
    private double _windowWidth = 1120;
    private double _windowHeight = 780;
    private double? _windowLeft;
    private double? _windowTop;
    private bool _clickThroughEnabled;
    private TeleprompterMode _teleprompterMode = TeleprompterMode.Manual;
    private double _manualScrollSpeed = 54;
    private int _timedDurationSeconds = 180;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("NoteText")]
    public string ScriptText
    {
        get => _scriptText;
        set => SetField(ref _scriptText, value);
    }

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => SetField(ref _overlayOpacity, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetField(ref _fontSize, value);
    }

    public string TextColorHex
    {
        get => _textColorHex;
        set => SetField(ref _textColorHex, value);
    }

    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set => SetField(ref _textAlignment, value);
    }

    public bool MirrorText
    {
        get => _mirrorText;
        set => SetField(ref _mirrorText, value);
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set => SetField(ref _windowWidth, value);
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set => SetField(ref _windowHeight, value);
    }

    public double? WindowLeft
    {
        get => _windowLeft;
        set => SetField(ref _windowLeft, value);
    }

    public double? WindowTop
    {
        get => _windowTop;
        set => SetField(ref _windowTop, value);
    }

    public bool ClickThroughEnabled
    {
        get => _clickThroughEnabled;
        set => SetField(ref _clickThroughEnabled, value);
    }

    public TeleprompterMode TeleprompterMode
    {
        get => _teleprompterMode;
        set => SetField(ref _teleprompterMode, value);
    }

    public double ManualScrollSpeed
    {
        get => _manualScrollSpeed;
        set => SetField(ref _manualScrollSpeed, value);
    }

    public int TimedDurationSeconds
    {
        get => _timedDurationSeconds;
        set => SetField(ref _timedDurationSeconds, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
