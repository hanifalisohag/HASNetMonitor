using System.Drawing;
using System.Text.RegularExpressions;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The application mark has to exist as a real multi-size .ico, be wired into the
/// build, and actually reach the exe - a file nobody embeds is no icon at all.
/// </summary>
public class AppIconTests
{
    private static readonly int[] RequiredSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [Fact]
    public void TheIconAssetHoldsEverySizeWindowsAsksFor()
    {
        byte[] icon = File.ReadAllBytes(IconPath());

        AssertInvariants(icon);
        List<int> sizes = ReadSizes(icon);
        foreach (int size in RequiredSizes)
        {
            Assert.Contains(size, sizes);
        }
    }

    [Fact]
    public void TheProjectEmbedsTheIconInTheExe()
    {
        string project = File.ReadAllText(Path.Combine(RepositoryRoot(), "HASNetMonitor.csproj"));

        Match declared = Regex.Match(
            project,
            @"<ApplicationIcon>\s*([^<]+?)\s*</ApplicationIcon>",
            RegexOptions.IgnoreCase);

        Assert.True(declared.Success, "the project must declare an ApplicationIcon");
        string relative = declared.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
        Assert.True(
            File.Exists(Path.Combine(RepositoryRoot(), relative)),
            $"ApplicationIcon points at '{relative}', which does not exist");
        Assert.Equal("Assets/HASNetMonitor.ico", relative.Replace('\\', '/'));
    }

    [Fact]
    public void TheBuiltExeReallyCarriesTheMark()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "HASNetMonitor.exe");
        Assert.True(File.Exists(exe), "the app exe should be copied next to the tests");

        using Icon? embedded = Icon.ExtractAssociatedIcon(exe);
        Assert.NotNull(embedded);

        (int green, int red, int opaque) = CountMark(embedded!);
        Assert.True(opaque > 0, "the exe icon must not be blank");
        Assert.True(green > 0 && red > 0, "the exe icon must be the green/red activity mark");
    }

    [Fact]
    public void TheIconLoadsFromTheAssemblyWithoutTouchingTheExe()
    {
        using Icon? icon = AppIcon.Load();
        Assert.NotNull(icon);

        (int green, int red, int opaque) = CountMark(icon!);
        Assert.True(opaque > 0);
        Assert.True(green > 0 && red > 0);
    }

    [Fact]
    public void TheSmallFramesAreStillReadable()
    {
        // A 16px frame that is mostly empty reads as a dot in Explorer.
        byte[] icon = File.ReadAllBytes(IconPath());
        AssertInvariants(icon);

        using var stream = new MemoryStream(icon, writable: false);
        using var image = new Icon(stream, new Size(16, 16));
        using Bitmap bitmap = image.ToBitmap();

        (_, _, int opaque) = CountMark(image);
        int total = bitmap.Width * bitmap.Height;
        Assert.True(opaque > total / 10, $"the 16px frame is too sparse: {opaque} of {total} pixels");
    }

    [Fact]
    public void TheSettingsDialogWearsTheMark()
    {
        Icon? shown = null;
        int green = 0;
        int red = 0;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(new Core.MonitorConfig());

                // The form owns the icon and disposes it, so inspect it while alive.
                shown = form.Icon;
                if (shown is not null)
                {
                    (green, red, _) = CountMark(shown);
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

        Assert.NotNull(shown);
        Assert.True(green > 0 && red > 0, "the dialog must show the application mark");
    }

    /// <summary>Structure that must hold for any icon the shell will accept.</summary>
    private static void AssertInvariants(byte[] icon)
    {
        Assert.True(icon.Length > 6, "the file is too small to be an icon");
        Assert.Equal(0, BitConverter.ToInt16(icon, 0));
        Assert.Equal(1, BitConverter.ToInt16(icon, 2));

        int count = BitConverter.ToInt16(icon, 4);
        Assert.InRange(count, 1, 64);
        Assert.Equal(6 + (16 * count), icon.Length > 6 + (16 * count) ? 6 + (16 * count) : icon.Length);

        foreach ((int width, int height, int bytes, int offset) in ReadEntries(icon))
        {
            Assert.Equal(width, height);
            Assert.True(width is > 0 and <= 256, $"bad edge {width}");
            Assert.True(bytes > 0, "an entry with no image data");
            Assert.True(offset >= 6 + (16 * count), "image data must follow the directory");
            Assert.True(offset + bytes <= icon.Length, "image data runs past the end of the file");
        }
    }

    private static List<(int Width, int Height, int Bytes, int Offset)> ReadEntries(byte[] icon)
    {
        int count = BitConverter.ToInt16(icon, 4);
        var entries = new List<(int, int, int, int)>(count);
        for (int i = 0; i < count; i++)
        {
            int at = 6 + (i * 16);
            int width = icon[at] == 0 ? 256 : icon[at];
            int height = icon[at + 1] == 0 ? 256 : icon[at + 1];
            int bytes = BitConverter.ToInt32(icon, at + 8);
            int offset = BitConverter.ToInt32(icon, at + 12);
            entries.Add((width, height, bytes, offset));
        }

        return entries;
    }

    private static List<int> ReadSizes(byte[] icon) =>
        ReadEntries(icon).Select(entry => entry.Width).OrderBy(edge => edge).ToList();

    private static (int Green, int Red, int Opaque) CountMark(Icon icon)
    {
        using Bitmap bitmap = icon.ToBitmap();
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

    private static string IconPath() =>
        Path.Combine(RepositoryRoot(), "Assets", "HASNetMonitor.ico");

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
