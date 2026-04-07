namespace TransparentPrompterOverlay.Services;

public interface IScrollViewport
{
    double CurrentOffset { get; }
    double MaxOffset { get; }
    void ScrollToOffset(double offset);
}
