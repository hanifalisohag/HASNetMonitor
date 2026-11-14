using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarNetMonitor.UI;

/// <summary>Temporary diagnostics for tracking hide/show and blink churn. Delete when done.</summary>
internal static class FrameTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string? Path = Environment.GetEnvironmentVariable("TNM_TRACE");

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    public static void Write(string message)
    {
        if (Path is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(Path, $"{Clock.ElapsedMilliseconds,7} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Samples the widget's own rectangle every few milliseconds and logs whenever the
    /// bright pixels (text and bars) vanish and come back, which is the blink a user sees.
    /// </summary>
    public static void StartBlinkWatch(Func<(int X, int Y, int W, int H)?> rectProvider)
    {
        if (Path is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            long? blankSince = null;
            int bright = -1;
            while (true)
            {
                if (rectProvider() is { } rect && rect.W > 0 && rect.H > 0)
                {
                    IntPtr dc = GetDC(IntPtr.Zero);
                    int lit = 0;
                    try
                    {
                        for (int y = 1; y < rect.H; y += 2)
                        {
                            for (int x = 1; x < rect.W; x += 2)
                            {
                                uint px = GetPixel(dc, rect.X + x, rect.Y + y);
                                int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
                                if (r > 120 && g > 120 && b > 120)
                                {
                                    lit++;
                                }
                            }
                        }
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, dc);
                    }

                    if (lit != bright)
                    {
                        Write($"screen lit={lit}");
                        bright = lit;
                    }

                    if (lit == 0 && blankSince is null)
                    {
                        blankSince = Clock.ElapsedMilliseconds;
                        Write("screen BLANK start");
                    }
                    else if (lit > 0 && blankSince is not null)
                    {
                        Write($"screen BLANK ended after {Clock.ElapsedMilliseconds - blankSince.Value} ms");
                        blankSince = null;
                    }
                }

                Thread.Sleep(4);
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
    }
}
