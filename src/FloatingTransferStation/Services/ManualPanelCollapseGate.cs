using System.Windows;

namespace FloatingTransferStation.Services;

public sealed class ManualPanelCollapseGate
{
    private Rect? _dismissedFrame;
    public bool SuppressesHover => _dismissedFrame is not null;
    public void Begin(Rect frame) => _dismissedFrame = frame;
    public void Reset() => _dismissedFrame = null;
    public void Observe(Point screenPoint)
    {
        if (_dismissedFrame is { } frame && !frame.Contains(screenPoint)) { Reset(); }
    }
}
