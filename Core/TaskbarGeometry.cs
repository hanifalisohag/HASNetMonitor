namespace TaskbarNetMonitor.Core;

internal enum TaskbarOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Pure placement maths for the widget. No Win32 calls happen here, so the exact
/// same code that positions the widget is exercised by the unit tests.
/// </summary>
internal static class TaskbarGeometry
{
    /// <summary>Smallest gap between the widget and the taskbar edge it is clamped against.</summary>
    public const int DefaultMargin = 2;

    /// <summary>Inset used when the notification area cannot be located at all.</summary>
    public const int TrayFallbackInset = 120;

    public static TaskbarOrientation OrientationOf(PixelRect taskbar)
        => taskbar.Width >= taskbar.Height ? TaskbarOrientation.Horizontal : TaskbarOrientation.Vertical;

    /// <summary>
    /// Size the widget occupies on screen. Vertical taskbars get the text rotated,
    /// so the horizontal layout size is simply swapped.
    /// </summary>
    public static Size WidgetSizeFor(TaskbarOrientation orientation, Size layoutSize)
        => orientation == TaskbarOrientation.Horizontal
            ? layoutSize
            : new Size(layoutSize.Height, layoutSize.Width);

    /// <summary>
    /// Places the widget immediately before the notification area, centred on the
    /// taskbar's short axis, then shifts it by the user's nudge. Returns null when the
    /// taskbar is degenerate or simply too small to hold the widget: the caller then
    /// hides the window instead of squashing it into a shape that cannot show anything.
    /// </summary>
    public static PixelRect? ComputePlacement(
        PixelRect taskbar,
        PixelRect? tray,
        Size layoutSize,
        int offsetFromTray,
        int margin = DefaultMargin,
        int nudgeX = 0,
        int nudgeY = 0)
    {
        if (taskbar.IsEmpty || layoutSize.Width <= 0 || layoutSize.Height <= 0)
        {
            return null;
        }

        margin = Math.Max(0, margin);
        offsetFromTray = Math.Max(0, offsetFromTray);

        var orientation = OrientationOf(taskbar);
        var widget = WidgetSizeFor(orientation, layoutSize);

        if (orientation == TaskbarOrientation.Horizontal)
        {
            if (widget.Width + margin * 2 > taskbar.Width)
            {
                return null;
            }

            int x;
            if (TryTrayEdge(tray, taskbar, orientation, out int trayNearEdge))
            {
                x = trayNearEdge - offsetFromTray - widget.Width;
            }
            else
            {
                x = taskbar.Right - TrayFallbackInset - widget.Width;
            }

            x = ClampInside(x, taskbar.Left + margin, taskbar.Right - margin - widget.Width);
            x = ClampInside(x + nudgeX, taskbar.Left, taskbar.Right - widget.Width);
            int y = CentredOnAxis(taskbar.Top, taskbar.Height, widget.Height, margin);
            y = ClampInside(y + nudgeY, taskbar.Top, taskbar.Bottom - widget.Height);
            return new PixelRect(x, y, widget.Width, widget.Height);
        }

        if (widget.Height + margin * 2 > taskbar.Height)
        {
            return null;
        }

        int widgetY;
        if (TryTrayEdge(tray, taskbar, orientation, out int trayTop))
        {
            widgetY = trayTop - offsetFromTray - widget.Height;
        }
        else
        {
            widgetY = taskbar.Bottom - TrayFallbackInset - widget.Height;
        }

        widgetY = ClampInside(widgetY, taskbar.Top + margin, taskbar.Bottom - margin - widget.Height);
        widgetY = ClampInside(widgetY + nudgeY, taskbar.Top, taskbar.Bottom - widget.Height);
        int widgetX = CentredOnAxis(taskbar.Left, taskbar.Width, widget.Width, margin);
        widgetX = ClampInside(widgetX + nudgeX, taskbar.Left, taskbar.Right - widget.Width);
        return new PixelRect(widgetX, widgetY, widget.Width, widget.Height);
    }

    /// <summary>
    /// Returns the coordinate (along the taskbar's long axis) where the notification
    /// area starts. The tray rectangle is only trusted when it actually lies inside
    /// the taskbar we were given; a stale or foreign rectangle is ignored so the
    /// widget never lands on another monitor.
    /// </summary>
    private static bool TryTrayEdge(PixelRect? tray, PixelRect taskbar, TaskbarOrientation orientation, out int edge)
    {
        edge = 0;
        if (tray is not { } rect || rect.IsEmpty)
        {
            return false;
        }

        if (orientation == TaskbarOrientation.Horizontal)
        {
            if (rect.Left < taskbar.Left || rect.Right > taskbar.Right + 1)
            {
                return false;
            }

            if (rect.Top < taskbar.Top - 1 || rect.Bottom > taskbar.Bottom + 1)
            {
                return false;
            }

            edge = rect.Left;
            return true;
        }

        if (rect.Top < taskbar.Top || rect.Bottom > taskbar.Bottom + 1)
        {
            return false;
        }

        if (rect.Left < taskbar.Left - 1 || rect.Right > taskbar.Right + 1)
        {
            return false;
        }

        edge = rect.Top;
        return true;
    }

    private static int CentredOnAxis(int start, int length, int itemLength, int margin)
    {
        int position = start + (length - itemLength) / 2;
        return ClampInside(position, start + margin, start + length - margin - itemLength);
    }

    private static int ClampInside(int value, int min, int max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }
}
