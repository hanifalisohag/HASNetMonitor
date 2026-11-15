using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The clock is drawn by the shell with ClearType on an opaque taskbar. These tests pin
/// the two things that stand between the widget and that look: a fractional font size,
/// which no renderer can grid-fit, and a transparent background, which leaves ClearType
/// nothing to compute its colour fringes against.
/// </summary>
public class CrispTextTests
{
    private static MonitorConfig Config(bool crisp) => new MonitorConfig
    {
        ShowText = true,
        FontSizePt = 9,
        CrispText = crisp,
    }.Normalize();

    /// <summary>
    /// The rules that must hold for any config: the glyphs are built from a whole-pixel
    /// em, and a crisp frame is opaque everywhere the plate covers. Called at the end of
    /// every test here. The plate has rounded corners, so the corners themselves stay
    /// transparent by design and are checked separately.
    /// </summary>
    private static void CheckInvariants(Bitmap frame, WidgetLayout layout, MonitorConfig config)
    {
        Assert.True(layout.FontEmPixels >= 1, $"em size {layout.FontEmPixels} is not a whole pixel");
        Assert.Equal(layout.Size.Width, frame.Width);
        Assert.Equal(layout.Size.Height, frame.Height);

        if (config.CrispText)
        {
            // ClearType only lands correctly on a surface that is opaque where it paints,
            // so the interior of the plate - everything inside the corner radius - must be
            // fully opaque.
            int inset = (int)Math.Ceiling(WidgetRenderer.CornerRadius(layout.Scale));
            var interior = new Rectangle(
                inset,
                inset,
                Math.Max(1, frame.Width - (2 * inset)),
                Math.Max(1, frame.Height - (2 * inset)));

            for (int y = interior.Top; y < interior.Bottom; y++)
            {
                for (int x = interior.Left; x < interior.Right; x++)
                {
                    Assert.Equal(255, frame.GetPixel(x, y).A);
                }
            }
        }
    }

    [Fact]
    public void EveryTaskbarHeightGivesAWholePixelFont()
    {
        var renderer = new WidgetRenderer();

        for (float scale = 1f; scale <= 2.01f; scale += 0.25f)
        {
            for (int taskbarHeight = 24; taskbarHeight <= 96; taskbarHeight++)
            {
                WidgetLayout layout = renderer.ComputeLayout(Config(false), scale, taskbarHeight);

                Assert.True(
                    layout.FontEmPixels >= 1,
                    $"em {layout.FontEmPixels} at scale {scale}, height {taskbarHeight}");

                // The font the renderer paints with is built from that integer and nothing
                // else, so its reported point size must round-trip back to it.
                using Font font = WidgetRenderer.CreateFont(layout.FontEmPixels, Config(false));
                Assert.Equal(GraphicsUnit.Pixel, font.Unit);
                Assert.Equal(layout.FontEmPixels, font.Size);
            }
        }
    }

    [Fact]
    public void ASqueezedTaskbarStepsDownOnePixelAtATime()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(false);

        WidgetLayout roomy = renderer.ComputeLayout(config, 1f, 60);
        WidgetLayout cramped = renderer.ComputeLayout(config, 1f, 36);

        Assert.True(roomy.FitsBounds);
        Assert.True(cramped.FitsBounds);
        Assert.True(cramped.FontEmPixels < roomy.FontEmPixels, "a shorter taskbar must use smaller glyphs");
        Assert.True(roomy.FontEmPixels - cramped.FontEmPixels >= 1);
    }

    [Fact]
    public void ThePointSizeNoLongerDecidesTheGlyphSize()
    {
        // 9pt on a 125% taskbar used to be 15.0 px, and after one shrink step 11.71 px -
        // a fraction, which is what smeared the stems. Both are whole now.
        var renderer = new WidgetRenderer();

        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
        {
            WidgetLayout layout = renderer.ComputeLayout(Config(false), scale, (int)Math.Round(48 * scale));

            using Font font = WidgetRenderer.CreateFont(layout.FontEmPixels, Config(false));
            Assert.Equal(GraphicsUnit.Pixel, font.Unit);
            Assert.Equal(0, font.Size % 1.0);
        }
    }

    [Fact]
    public void CrispTextIsRealSubpixelRendering()
    {
        // ClearType puts different amounts of red, green and blue on the two sides of a
        // stem. Greyscale antialiasing never does: its pixels are always neutral. So a
        // coloured pixel on a glyph edge is proof the glyphs went through ClearType.
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(crisp: true);
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);

        using Bitmap frame = renderer.Render(WidgetVisual.Empty, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        Assert.True(ColouredGlyphPixels(frame, layout) > 20, "no subpixel fringes: this is greyscale, not ClearType");
        CheckInvariants(frame, layout, config);
    }

    [Fact]
    public void WithoutCrispTextTheGlyphsStayGreyscale()
    {
        // The widget must not silently start baking black-fringed ClearType glyphs into a
        // transparent surface: those fringes would halo over a light taskbar.
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(crisp: false);
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);

        using Bitmap frame = renderer.Render(WidgetVisual.Empty, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        Assert.Equal(0, ColouredGlyphPixels(frame, layout));
        CheckInvariants(frame, layout, config);
    }

    [Fact]
    public void AClearTypeFrameIsOpaqueUnderTheTextAndRoundedAtTheCorners()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(crisp: true);
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);

        using Bitmap frame = renderer.Render(WidgetVisual.Empty, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        // The plate is a rounded rectangle, so the corner pixel is outside it while the
        // pixel under the glyphs is inside. Both matter: opaque text, soft corner.
        Assert.Equal(255, frame.GetPixel(frame.Width / 2, frame.Height / 2).A);
        Assert.True(frame.GetPixel(0, 0).A < 128, "the plate must keep its rounded corner");
        CheckInvariants(frame, layout, config);
    }

    [Fact]
    public void CrispTextImposesItsOwnPlateWhateverTheTintSays()
    {
        // A half-transparent plate would leave the fringes computed against the wrong
        // colour, so the crisp mode must not inherit a low tint opacity.
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(crisp: true);
        config.ShowBackdrop = false;
        config.BackdropOpacity = 40;
        config = config.Normalize();

        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        using Bitmap frame = renderer.Render(WidgetVisual.Empty, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        Assert.Equal(255, frame.GetPixel(frame.Width / 2, frame.Height / 2).A);
        Assert.True(ColouredGlyphPixels(frame, layout) > 0);
        CheckInvariants(frame, layout, config);
    }

    [Fact]
    public void AnOpaqueTintIsCrispEvenWithoutTheCheckbox()
    {
        // The tint at full opacity already gives ClearType the background it needs, so
        // the checkbox is a shortcut, not the only route.
        var renderer = new WidgetRenderer();
        MonitorConfig config = Config(crisp: false);
        config.ShowBackdrop = true;
        config.BackdropOpacity = WidgetRenderer.OpaqueAlpha;
        config = config.Normalize();

        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        using Bitmap frame = renderer.Render(WidgetVisual.Empty, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        Assert.True(ColouredGlyphPixels(frame, layout) > 0, "an opaque tint must switch the glyphs to ClearType");
        CheckInvariants(frame, layout, config);
    }

    [Fact]
    public void CrispTextSurvivesTheConfigRoundTripOnBothBackends()
    {
        foreach (bool crisp in new[] { true, false })
        {
            MonitorConfig config = Config(crisp);
            MonitorConfig loaded = MonitorConfig.FromJson(config.ToJson());

            Assert.Equal(crisp, loaded.CrispText);
            Assert.Contains("\"CrispText\"", config.ToJson());
        }
    }

    [Fact]
    public void CrispTextIsOffByDefaultBecauseItHidesTheTaskbar()
    {
        // On by default would quietly cover the taskbar behind every existing install.
        Assert.False(new MonitorConfig().CrispText);
        Assert.False(new MonitorConfig().Normalize().CrispText);
    }

    [Fact]
    public void CloneAndCopyKeepTheChoice()
    {
        MonitorConfig source = Config(crisp: true);

        Assert.True(source.Clone().CrispText);

        var target = new MonitorConfig { CrispText = false };
        target.CopyFrom(source);
        Assert.True(target.CrispText);
    }

    [Fact]
    public void TheDialogOffersTheChoiceAndKeepsTheTintControlsHonest()
    {
        MonitorConfig plain = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            MonitorConfig before = form.BuildResult();

            // Off by default: no plate, tint opacity editable.
            Assert.False(before.CrispText);
            Assert.True(Writable(form, "_backdropOpacity"), "opacity must be editable while smooth");

            // Ticking it must force the plate opaque and lock the spinner that can no
            // longer apply, so the dialog never shows a setting the renderer overrides.
            Check(form, "_crispText").Checked = true;

            Assert.Equal(255, (decimal)Field(form, "_backdropOpacity", "Value"));
            Assert.False(Writable(form, "_backdropOpacity"));
            Assert.True(Check(form, "_showBackdrop").Checked);

            MonitorConfig after = form.BuildResult();
            Assert.True(after.CrispText);
            Assert.True(after.ShowBackdrop);
            Assert.Equal(255, after.BackdropOpacity);

            // And unticking gives the control back.
            Check(form, "_crispText").Checked = false;
            Assert.True(Writable(form, "_backdropOpacity"));
            Assert.False(form.BuildResult().CrispText);

            return after;
        });

        Assert.True(plain.CrispText);
    }

    [Fact]
    public void AReloadedCrispConfigOpensCrisp()
    {
        MonitorConfig saved = Config(crisp: true);

        MonitorConfig reopened = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(saved.Clone());
            Assert.True(Check(form, "_crispText").Checked);
            return form.BuildResult();
        });

        Assert.True(reopened.CrispText);
        Assert.Equal(255, reopened.BackdropOpacity);
    }

    private static CheckBox Check(SettingsForm form, string name) => (CheckBox)Field(form, name);

    private static bool Writable(SettingsForm form, string name) => ((NumericUpDown)Field(form, name)).Enabled;

    private static object Field(SettingsForm form, string name, string? property = null)
    {
        object? value = typeof(SettingsForm)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(form);
        Assert.NotNull(value);

        return property is null
            ? value!
            : value!.GetType().GetProperty(property)!.GetValue(value)!;
    }

    private static T RunOnUiThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the dialog did not finish opening");

        if (failure is not null)
        {
            throw new InvalidOperationException("the settings dialog failed on the UI thread", failure);
        }

        return result;
    }

    /// <summary>
    /// Pixels where red, green and blue genuinely differ: ClearType fringes. Only the
    /// figures are scanned - the direction arrows are deliberately coloured green and
    /// red, and counting them would say "subpixel" about any widget.
    /// </summary>
    private static int ColouredGlyphPixels(Bitmap frame, WidgetLayout layout)
    {
        int coloured = 0;

        using var surface = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(layout.FontEmPixels);
        using StringFormat format = new StringFormat(StringFormat.GenericTypographic);
        int arrowWidth = (int)Math.Ceiling(WidgetRenderer.RowNumberOffset(graphics, font, format, layout.Scale));

        foreach (Rectangle row in new[] { layout.DownloadText, layout.UploadText })
        {
            int left = Math.Max(row.Left, row.Left + arrowWidth);
            for (int y = Math.Max(0, row.Top); y < Math.Min(frame.Height, row.Bottom); y++)
            {
                for (int x = left; x < Math.Min(frame.Width, row.Right); x++)
                {
                    Color pixel = frame.GetPixel(x, y);
                    int spread = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B))
                        - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                    if (spread > 25 && pixel.A > 40)
                    {
                        coloured++;
                    }
                }
            }
        }

        return coloured;
    }
}
