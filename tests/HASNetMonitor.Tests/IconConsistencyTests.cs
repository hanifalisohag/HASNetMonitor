using System.Drawing;
using System.Drawing.Imaging;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The tray mark, the exe icon and the settings-dialog icon must be one drawing. They
/// used to be two: the tray drew its own arrows with GDI+ while the exe shipped the
/// .ico, so the same app showed two different glyphs in two places.
/// </summary>
public class IconConsistencyTests
{
    [Fact]
    public void TheTrayIconIsTheExeIconPixelForPixel()
    {
        using Icon trayIcon = TrayMark(32, 32);
        using Icon? exeIcon = Icon.ExtractAssociatedIcon(ExePath());
        Assert.NotNull(exeIcon);

        AssertSamePixels(trayIcon, exeIcon!, "the tray icon and the exe icon");
    }

    [Fact]
    public void TheTrayIconMatchesTheIconAssetAtEveryShellSize()
    {
        byte[] asset = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "Assets", "HASNetMonitor.ico"));

        foreach (int edge in new[] { 16, 20, 24, 32, 40, 48 })
        {
            using var stream = new MemoryStream(asset, writable: false);
            using var fromAsset = new Icon(stream, edge, edge);
            using Icon tray = TrayMark(edge, edge);

            AssertSamePixels(tray, fromAsset, $"the {edge}px tray icon and the {edge}px asset frame");
        }
    }

    [Fact]
    public void TheTrayIconFollowsTheSizeTheShellAsksFor()
    {
        foreach (int edge in new[] { 16, 24, 32, 40 })
        {
            using Icon icon = TrayMark(edge, edge);
            using Bitmap bitmap = icon.ToBitmap();
            Assert.Equal(edge, bitmap.Width);
            Assert.Equal(edge, bitmap.Height);
        }
    }

    [Fact]
    public void ATinyRequestStillProducesAUsableMark()
    {
        // The shell reports small sizes it has never reported before on odd DPIs; the
        // tray must clamp to a readable size instead of failing or returning nothing.
        using Icon icon = TrayMark(4, 4);
        using Bitmap bitmap = icon.ToBitmap();

        Assert.True(bitmap.Width >= 16, $"a 4px request produced a {bitmap.Width}px icon");
        Assert.Equal(bitmap.Width, bitmap.Height);

        (int green, int red, int opaque) = Count(bitmap);
        Assert.True(opaque > 0, "the tray icon must not be blank");
        Assert.True(green > 0 && red > 0, "the tray icon must be the green/red mark");
    }

    [Fact]
    public void TheTrayAndTheSettingsDialogShowTheSameMark()
    {
        using Icon tray = TrayMark(32, 32);
        using Bitmap trayBitmap = tray.ToBitmap();

        Icon? dialogIcon = null;
        Bitmap? dialogBitmap = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(new Core.MonitorConfig());
                dialogIcon = form.Icon;
                if (dialogIcon is not null)
                {
                    dialogBitmap = dialogIcon.ToBitmap();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "opening the dialog timed out");
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        using (dialogBitmap)
        {
            Assert.NotNull(dialogBitmap);
            AssertSamePixels(trayBitmap, dialogBitmap!, "the tray icon and the dialog icon");
        }
    }

    [Fact]
    public void TheTrayNoLongerDrawsItsOwnGlyph()
    {
        // Guard against the regression this test file exists for: a second, hand-drawn
        // mark in the tray code. The only drawing may live in the .ico.
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "UI", "MonitorHost.cs"));
        Assert.DoesNotContain("DrawLines", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHicon", source, StringComparison.Ordinal);
        Assert.Contains("AppIcon.LoadForSize", source, StringComparison.Ordinal);
    }

    private static Icon TrayMark(int width, int height)
    {
        Icon? icon = MonitorHost.CreateTrayMark(new Size(width, height));
        Assert.NotNull(icon);
        return icon!;
    }

    private static void AssertSamePixels(Icon expected, Icon actual, string what)
    {
        using Bitmap left = expected.ToBitmap();
        using Bitmap right = actual.ToBitmap();
        AssertSamePixels(left, right, what);
    }

    private static void AssertSamePixels(Bitmap expected, Bitmap actual, string what)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        int differing = 0;
        int firstX = -1;
        int firstY = -1;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (expected.GetPixel(x, y) == actual.GetPixel(x, y))
                {
                    continue;
                }

                if (differing == 0)
                {
                    firstX = x;
                    firstY = y;
                }

                differing++;
            }
        }

        Assert.True(
            differing == 0,
            $"{what} differ in {differing} pixels (first at {firstX},{firstY})");
    }

    private static (int Green, int Red, int Opaque) Count(Bitmap bitmap)
    {
        int green = 0;
        int red = 0;
        int opaque = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A <= 40)
                {
                    continue;
                }

                opaque++;
                if (pixel.G > 120 && pixel.G > pixel.R + 40 && pixel.G > pixel.B + 40)
                {
                    green++;
                }
                else if (pixel.R > 120 && pixel.R > pixel.G + 40 && pixel.R > pixel.B + 40)
                {
                    red++;
                }
            }
        }

        return (green, red, opaque);
    }

    private static string ExePath() =>
        Path.Combine(AppContext.BaseDirectory, "HASNetMonitor.exe");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HASNetMonitor.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not find the project folder from " + AppContext.BaseDirectory);
    }
}
