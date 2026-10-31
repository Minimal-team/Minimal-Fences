namespace MinimalFences.Core;

public readonly record struct ScreenArea(double X, double Y, double Width, double Height, double Scale = 1)
{
    public bool ContainsTitle(Group group) =>
        group.X + Math.Min(group.Width * Scale, 80) > X && group.X < X + Width - 30 &&
        group.Y + 28 * Scale > Y && group.Y < Y + Height - 28;
}

public static class LayoutGeometry
{
    public static void RecoverOffscreen(IEnumerable<Group> groups, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) return;
        int offset = 0;
        foreach (var group in groups)
        {
            var match = screens.FirstOrDefault(s => s.ContainsTitle(group));
            if (match != default)
            {
                ClampToScreen(group, match);
            }
            else
            {
                MoveToScreen(group, screens[0], offset++ % 8 * 24);
            }
        }
    }

    public static void ClampToScreen(Group group, ScreenArea screen)
    {
        double maxX = screen.X + screen.Width - 60;
        double maxY = screen.Y + screen.Height - 40;
        group.X = Math.Clamp(group.X, screen.X, Math.Max(screen.X, maxX));
        group.Y = Math.Clamp(group.Y, screen.Y, Math.Max(screen.Y, maxY));
    }

    public static void MoveToScreen(Group group, ScreenArea screen, int offset = 0)
    {
        group.Width = Math.Min(group.Width, Math.Max(180, (screen.Width - 32) / screen.Scale));
        group.Height = Math.Min(group.Height, Math.Max(140, (screen.Height - 32) / screen.Scale));
        group.X = screen.X + Math.Min(24 + offset, Math.Max(0, screen.Width - group.Width * screen.Scale));
        group.Y = screen.Y + Math.Min(24 + offset, Math.Max(0, screen.Height - group.Height * screen.Scale));
    }
}
