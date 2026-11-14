using System.Drawing;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.Rendering;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor;

/// <summary>
/// Headless smoke test of the pieces that can be exercised without a screen:
/// formatting, rate maths, config handling, taskbar geometry and the actual pixels
/// the renderer produces. Run it with <c>HASNetMonitor.exe --self-test</c>.
/// </summary>
internal static class SelfTest
{
    public static bool Run(TextWriter writer)
    {
        int failures = 0;

        void Check(string name, Func<bool> assertion)
        {
            bool passed;
            try
            {
                passed = assertion();
            }
            catch (Exception ex)
            {
                writer.WriteLine($"[fail] {name}: {ex.GetType().Name}: {ex.Message}");
                failures++;
                return;
            }

            writer.WriteLine(passed ? $"[ok]   {name}" : $"[fail] {name}");
            if (!passed)
            {
                failures++;
            }
        }

        Check("formatter: zero bytes", () => SpeedFormatter.Format(0) == "0 B/s");
        Check("formatter: KiB", () => SpeedFormatter.Format(1536) == "1.5 KB/s");
        Check("formatter: NaN is treated as zero", () => SpeedFormatter.Format(double.NaN) == "0 B/s");

        Check("rates: first sample is zero", () =>
        {
            var calculator = new RateCalculator();
            return calculator.Update(1000, 1.0) == 0;
        });

        Check("rates: bytes over elapsed time", () =>
        {
            var calculator = new RateCalculator(smoothingFactor: 1.0);
            calculator.Update(0, 1.0);
            return Math.Abs(calculator.Update(2000, 2.0) - 1000) < 0.001;
        });

        Check("rates: counter reset does not go negative", () =>
        {
            var calculator = new RateCalculator(smoothingFactor: 1.0);
            calculator.Update(5000, 1.0);
            return calculator.Update(10, 1.0) == 0;
        });

        Check("scaler: ratios stay inside the bar", () =>
        {
            var scaler = new AutoScaler();
            scaler.Update(10_000_000, 1_000_000, 1.0, autoScale: true, manualPeakBytesPerSecond: 1);
            double download = scaler.DownloadRatio(10_000_000);
            double upload = scaler.UploadRatio(1_000_000);
            return download is >= 0 and <= 1 && upload is >= 0 and <= 1;
        });

        string tempDirectory = Path.Combine(Path.GetTempPath(), "tnm-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDirectory);

            Check("config: round trip", () =>
            {
                string path = Path.Combine(tempDirectory, "roundtrip.json");
                var original = new MonitorConfig { DownloadColorHex = "#123456", RefreshIntervalMs = 500 }.Normalize();
                if (!original.TrySave(path))
                {
                    return false;
                }

                MonitorConfig loaded = MonitorConfig.Load(path);
                return loaded.DownloadColorHex == "#123456" && loaded.RefreshIntervalMs == 500;
            });

            Check("config: broken file falls back to defaults", () =>
            {
                string path = Path.Combine(tempDirectory, "broken.json");
                File.WriteAllText(path, "{ not json at all ");
                return MonitorConfig.Load(path).RefreshIntervalMs == MonitorConfig.DefaultRefreshIntervalMs;
            });

            Check("config: out-of-range values are clamped", () =>
            {
                MonitorConfig clamped = new MonitorConfig { RefreshIntervalMs = 1, BarThicknessPx = 99, FontSizePt = 500 }
                    .Normalize();
                return clamped.RefreshIntervalMs == MonitorConfig.MinRefreshIntervalMs
                       && clamped.BarThicknessPx == MonitorConfig.MaxBarThicknessPx
                       && clamped.FontSizePt == MonitorConfig.MaxFontSizePt;
            });

            Check("config: invalid colour falls back to the default", () =>
                new MonitorConfig { DownloadColorHex = "not-a-colour" }.Normalize().DownloadColorHex == MonitorConfig.DefaultDownloadColor);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        Check("geometry: sits left of the tray, vertically centred", () =>
        {
            var taskbar = new PixelRect(0, 1040, 1920, 48);
            var tray = new PixelRect(1700, 1040, 220, 48);
            var size = new Size(110, 40);
            PixelRect? placement = TaskbarGeometry.ComputePlacement(taskbar, tray, size, 6);
            return placement is { } rect
                   && rect.Left == 1700 - 6 - 110
                   && rect.Top == 1044
                   && rect.Width == 110
                   && rect.Height == 40;
        });

        Check("geometry: vertical taskbar is rotated and placed above the tray", () =>
        {
            var taskbar = new PixelRect(0, 0, 48, 1080);
            var tray = new PixelRect(0, 780, 48, 300);
            var size = new Size(110, 40);
            PixelRect? placement = TaskbarGeometry.ComputePlacement(taskbar, tray, size, 6);
            return placement is { } rect
                   && rect.Width == 40
                   && rect.Height == 110
                   && rect.Top == 780 - 6 - 110;
        });

        Check("geometry: refuses to draw when the taskbar cannot fit the widget", () =>
            TaskbarGeometry.ComputePlacement(new PixelRect(0, 0, 60, 48), null, new Size(110, 40), 6) is null);

        Check("render: download fill is the configured green", () =>
        {
            var renderer = new WidgetRenderer();
            MonitorConfig config = BarsOnly("#00FF00", "#FF0000");
            WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
            using Bitmap frame = renderer.Render(
                new WidgetVisual(1000, 2000, 0.5, 1.0), config, layout, new WidgetTheme(true), TaskbarOrientation.Horizontal);

            Color fill = frame.GetPixel(layout.DownloadBar.X + 1, layout.DownloadBar.Y + layout.DownloadBar.Height / 2);
            Color beyond = frame.GetPixel(layout.DownloadBar.Right - 2, layout.DownloadBar.Y + layout.DownloadBar.Height / 2);
            Color upload = frame.GetPixel(layout.UploadBar.X + 1, layout.UploadBar.Y + layout.UploadBar.Height / 2);

            return fill.A == 255 && fill.R == 0 && fill.G == 255 && fill.B == 0
                   && beyond.A is > 20 and < 120
                   && upload.A == 255 && upload.R == 255 && upload.G == 0 && upload.B == 0;
        });

        Check("render: background stays transparent like the clock", () =>
        {
            var renderer = new WidgetRenderer();
            MonitorConfig config = BarsOnly("#00FF00", "#FF0000");
            WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
            using Bitmap frame = renderer.Render(
                WidgetVisual.Empty, config, layout, new WidgetTheme(true), TaskbarOrientation.Horizontal);

            Color corner = frame.GetPixel(0, 0);
            Color middle = frame.GetPixel(frame.Width / 2, layout.DownloadBar.Y - 2);
            return corner.A <= 2 && middle.A <= 2;
        });

        Check("window: the presented frame owns its pixels on screen", () =>
        {
            using var window = new LayeredWindow();

            using var frame = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.FromArgb(255, 0, 255, 0));
            }

            // Off the taskbar so the check never races the real widget.
            window.Present(frame, 8, 8);

            return window.IsShown
                   && window.IsActuallyVisible
                   && window.SurfaceCentre is { } centre
                   && window.IsOnTopAt(centre);
        });

        Check("formatting: speeds match the row text", () =>
            SpeedFormatter.FormatRow(true, 2048) == "\u2193 2.0 KB/s"
            && SpeedFormatter.FormatRow(false, 0) == "\u2191 0 B/s");

        // Preview: renders one frame with text and partial bars and saves it, so the
        // layout can be inspected without a taskbar.
        {
            var renderer = new WidgetRenderer();
            MonitorConfig config = new MonitorConfig().Normalize();
            WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
            var visual = new WidgetVisual(1250, 310, 0.55, 0.2);
            using Bitmap preview = renderer.Render(
                visual, config, layout, new WidgetTheme(true), TaskbarOrientation.Horizontal);

            writer.WriteLine(
                $"[info] layout={layout.Size.Width}x{layout.Size.Height} pad={layout.PaddingX} "
                + $"barThickness={layout.BarThickness} downloadBar={layout.DownloadBar} uploadBar={layout.UploadBar} "
                + $"downloadText={layout.DownloadText}");

            string previewPath = Path.Combine(Path.GetTempPath(), "taskbar-monitor-preview.png");
            try
            {
                preview.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
                writer.WriteLine($"[info] preview saved to {previewPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        Check("interop: the taskbar-created broadcast message registers", () =>
            NativeMethods.RegisterWindowMessage("TaskbarCreated") != 0);

        Check("settings: dialog builds from a config and returns a copy", () =>
        {
            MonitorConfig source = new MonitorConfig().Normalize();
            using var form = new SettingsForm(source);
            MonitorConfig built = form.BuildResult();

            return form.Result is null                       // nothing accepted yet
                   && !ReferenceEquals(built, source)
                   && built.DownloadColorHex == source.DownloadColorHex
                   && built.UploadColorHex == source.UploadColorHex
                   && built.RefreshIntervalMs == source.RefreshIntervalMs
                   && built.ShowText == source.ShowText
                   && built.BarThicknessPx == source.BarThicknessPx;
        });

        // Live environment report: not a hard requirement (the taskbar may not exist
        // in a headless session), but it makes DPI and layout problems visible.
        if (TaskbarLocator.TryLocate(out TaskbarSnapshot snapshot))
        {
            var renderer = new WidgetRenderer();
            MonitorConfig config = new MonitorConfig().Normalize();
            int maxExtent = snapshot.Orientation == TaskbarOrientation.Horizontal
                ? snapshot.TaskbarRect.Height
                : snapshot.TaskbarRect.Width;

            WidgetLayout layout = renderer.ComputeLayout(config, snapshot.DpiScale, maxExtent);
            writer.WriteLine(
                $"[info] taskbar={snapshot.TaskbarRect} tray={snapshot.TrayRect?.ToString() ?? "<none>"} "
                + $"dpi={snapshot.DpiScale:0.##} widget={layout.Size.Width}x{layout.Size.Height} "
                + $"font={layout.FontSizePoints:0.##}pt maxExtent={maxExtent}");

            Check("layout: widget fits the taskbar height", () => layout.Size.Height <= maxExtent);
            Check("layout: widget fits the taskbar", () =>
                TaskbarGeometry.ComputePlacement(
                    snapshot.TaskbarRect,
                    snapshot.TrayRect,
                    layout.Size,
                    config.OffsetFromTrayPx) is not null);
            Check("layout: font is not squeezed below the minimum",
                () => layout.FontSizePoints >= WidgetRenderer.MinimumFontSizePt);
        }
        else
        {
            writer.WriteLine("[info] taskbar not found - skipping live layout checks");
        }

        if (failures == 0)
        {
            writer.WriteLine("self-test: PASS");
            return true;
        }

        writer.WriteLine($"self-test: FAIL ({failures} check(s))");
        return false;
    }

    private static MonitorConfig BarsOnly(string download, string upload)
    {
        var config = new MonitorConfig
        {
            DownloadColorHex = download,
            UploadColorHex = upload,
            ShowText = false,
            ShowBackdrop = false,
            BarThicknessPx = 4,
        };

        return config.Normalize();
    }
}
