namespace WinInspector.Core.Gui;

/// <summary>
/// Fits a saved window rectangle into the work area of the screen it is shown on.
/// Pure arithmetic: the Cli side reads the screen and passes its work area in as a rectangle.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Returns the saved rectangle moved and, if needed, shrunk so that it lies inside <paramref name="workArea"/>.
    /// The minimum size is kept when the work area is large enough; when the work area is smaller than the minimum,
    /// the window fits the work area instead (the screen is the stronger limit).
    /// </summary>
    public static WindowBounds FitToWorkArea(WindowBounds saved, WindowBounds workArea, int minWidth, int minHeight)
    {
        var width = Math.Min(Math.Max(saved.Width, minWidth), workArea.Width);
        var height = Math.Min(Math.Max(saved.Height, minHeight), workArea.Height);

        // long arithmetic: saved values come from a JSON file and may be close to int.MaxValue.
        long left = workArea.X;
        long top = workArea.Y;
        long right = left + workArea.Width;
        long bottom = top + workArea.Height;
        var x = Math.Min(Math.Max((long)saved.X, left), right - width);
        var y = Math.Min(Math.Max((long)saved.Y, top), bottom - height);
        return new WindowBounds((int)x, (int)y, width, height);
    }
}
