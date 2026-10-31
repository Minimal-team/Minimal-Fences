using MinimalFences.Core;
using Xunit;

namespace MinimalFences.Core.Tests;

public sealed class DesktopTests
{
    [Fact]
    public void TwoEmptyDesktopClicksToggleOnce()
    {
        var tracker = new DesktopClickTracker(500, 4, 4);
        Assert.False(tracker.Click(100, 400, 500, true));
        Assert.True(tracker.Click(400, 401, 501, true));
        Assert.False(tracker.Click(450, 401, 501, true));
    }
    [Fact]
    public void DesktopIconsApplicationWindowsAndGroupsResetTheSequence()
    {
        var tracker = new DesktopClickTracker(500, 4, 4);
        Assert.False(tracker.Click(100, 400, 500, true));
        Assert.False(tracker.Click(150, 400, 500, false));
        Assert.False(tracker.Click(200, 400, 500, true));
        Assert.True(tracker.Click(300, 400, 500, true));
    }
    [Theory]
    [InlineData(501, 0, 0)]
    [InlineData(100, 3, 0)]
    [InlineData(100, 0, 3)]
    public void SlowOrDistantClicksDoNotToggle(uint elapsed, int dx, int dy)
    {
        var tracker = new DesktopClickTracker(500, 4, 4);
        tracker.Click(100, 200, 200, true);
        Assert.False(tracker.Click(100 + elapsed, 200 + dx, 200 + dy, true));
    }
    [Fact]
    public void ClickTimestampsHandleWindowsTickRollover()
    {
        var tracker = new DesktopClickTracker(500, 4, 4);
        tracker.Click(uint.MaxValue - 100, 1, 1, true);
        Assert.True(tracker.Click(50, 1, 1, true));
    }
    [Fact]
    public void ResetPreventsAStaleDoubleClick()
    {
        var tracker = new DesktopClickTracker(500, 4, 4);
        tracker.Click(10, 1, 1, true);
        tracker.Reset();
        Assert.False(tracker.Click(20, 1, 1, true));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void DisconnectedMonitorLayoutIsRecoveredAtSupportedScale(double scale)
    {
        var group = new Group { X = -2500, Y = 1200, Width = 2000, Height = 2000 };
        var screen = new ScreenArea(0, 0, 1920, 1040, scale);
        LayoutGeometry.RecoverOffscreen([group], [screen]);
        Assert.True(screen.ContainsTitle(group));
        Assert.InRange(group.X + group.Width * scale, 0, 1920);
        Assert.InRange(group.Y + group.Height * scale, 0, 1040);
    }
    [Fact]
    public void SecondaryMonitorWithNegativeCoordinatesIsPreserved()
    {
        var group = new Group { X = -1800, Y = 40 };
        LayoutGeometry.RecoverOffscreen([group], [new(0, 0, 1920, 1080), new(-1920, 0, 1920, 1080)]);
        Assert.Equal(-1800, group.X);
        Assert.Equal(40, group.Y);
    }
}
