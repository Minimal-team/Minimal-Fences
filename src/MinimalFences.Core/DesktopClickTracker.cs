namespace MinimalFences.Core;

public sealed class DesktopClickTracker(uint interval, int width, int height)
{
    private (uint Time, int X, int Y)? _previous;
    public bool Click(uint time, int x, int y, bool emptyDesktop)
    {
        if (!emptyDesktop) { Reset(); return false; }
        var previous = _previous;
        _previous = (time, x, y);
        if (previous is not { } p || unchecked(time - p.Time) > interval ||
            Math.Abs((long)x - p.X) > width / 2 || Math.Abs((long)y - p.Y) > height / 2) return false;
        Reset();
        return true;
    }
    public void Reset() => _previous = null;
}
