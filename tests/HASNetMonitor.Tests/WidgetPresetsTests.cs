using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>
/// The preset gallery the settings dialog offers. A preset is a promise that one click
/// gives a coherent, drawable look, so these tests check that promise rather than the
/// individual colours: every entry applies, every entry is distinguishable, applying one
/// never changes what the widget measures or where it sits, and the dialog can tell which
/// preset - if any - the current settings represent.
/// </summary>
public class WidgetPresetsTests
{
    /// <summary>The gallery must be a real choice, not a token two-entry list.</summary>
    [Fact]
    public void TheGalleryOffersMoreThanTwentyLooks()
    {
        Assert.True(
            WidgetPresets.All.Count >= 20,
            $"the gallery has {WidgetPresets.All.Count} presets, which is not the 20+ the dialog promises");
    }

    [Fact]
    public void EveryPresetHasAUniqueNameAndADescription()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name), "a preset has no name");
            Assert.True(seen.Add(preset.Name), $"duplicate preset name: {preset.Name}");

            // The picker shows the description, so an empty one leaves a blank panel.
            Assert.False(
                string.IsNullOrWhiteSpace(preset.Description),
                $"{preset.Name} has no description");
        }

        Assert.Equal(WidgetPresets.All.Count, WidgetPresets.Names.Count);
    }

    /// <summary>
    /// The name is the only handle the dialog has, so lookup has to be exact enough to be
    /// safe and forgiving enough for the casing a config file may carry.
    /// </summary>
    [Fact]
    public void APresetIsFoundByNameWhateverTheCasing()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            Assert.Same(preset, WidgetPresets.Find(preset.Name));
            Assert.Same(preset, WidgetPresets.Find(preset.Name.ToUpperInvariant()));
            Assert.Same(preset, WidgetPresets.Find(preset.Name.ToLowerInvariant()));
            Assert.Same(preset, WidgetPresets.Find($"  {preset.Name}  "));
        }

        Assert.Null(WidgetPresets.Find(null));
        Assert.Null(WidgetPresets.Find(string.Empty));
        Assert.Null(WidgetPresets.Find("No Such Preset"));
    }

    /// <summary>
    /// Every preset must produce a config the renderer can draw, and the values it asks
    /// for must survive normalisation unchanged - otherwise the dialog would show a
    /// preset as selected while the widget drew something else.
    /// </summary>
    [Fact]
    public void EveryPresetAppliesToADrawableConfig()
    {
        MonitorConfig start = new MonitorConfig();

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, start);

            Assert.Equal(preset.DownloadColor, applied.DownloadColorHex);
            Assert.Equal(preset.UploadColor, applied.UploadColorHex);
            Assert.Equal(preset.Style, applied.Style);
            Assert.Equal(preset.ArrowStyle, applied.ArrowStyle);
            Assert.Equal(preset.SortOrder, applied.SortOrder);
            Assert.Equal(preset.BarThicknessPx, applied.BarThicknessPx);
            Assert.Equal(preset.SegmentGapPx, applied.SegmentGapPx);
            Assert.Equal(preset.FontSizePt, applied.FontSizePt, 3);
            Assert.Equal(preset.ArrowSizePercent, applied.ArrowSizePercent);
            Assert.Equal(preset.Bold, applied.FontBold);
            Assert.Equal(preset.Italic, applied.FontItalic);
            Assert.Equal(preset.RoundedBars, applied.RoundedBars);
            Assert.Equal(preset.AccentGlow, applied.AccentGlow);
            Assert.Equal(preset.ShowBackdrop, applied.ShowBackdrop);
            Assert.Equal(preset.CrispText, applied.CrispText);

            // The colour has to be a real hex value, not a name the parser guessed at.
            Assert.Matches("^#[0-9A-F]{6}$", applied.DownloadColorHex);
            Assert.Matches("^#[0-9A-F]{6}$", applied.UploadColorHex);

            // And the result must already be normalised, so applying it again is a no-op.
            MonitorConfig again = WidgetPresets.Apply(preset, applied);
            Assert.True(
                WidgetPresets.AppearanceEquals(applied, again),
                $"{preset.Name} is not stable: applying it twice changes the look");
        }
    }

    /// <summary>
    /// Applying a preset is a look change and nothing else. Moving the widget, changing
    /// how often it polls or turning off the logon entry are not appearance, and a preset
    /// that quietly reset them would undo work the user cannot see happening.
    /// </summary>
    [Fact]
    public void ApplyingAPresetLeavesBehaviourAndPlacementAlone()
    {
        MonitorConfig custom = new MonitorConfig
        {
            RefreshIntervalMs = 750,
            AutoScale = true,
            SharedScale = true,
            MaxScaleMbps = 250,
            NudgeXPx = 12,
            NudgeYPx = -4,
            OffsetFromTrayPx = 9,
            ShowText = false,
            StartWithWindows = true,
            FontFamily = FontCatalog.Automatic,
        }.Normalize();

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, custom);

            Assert.Equal(750, applied.RefreshIntervalMs);
            Assert.True(applied.AutoScale);
            Assert.True(applied.SharedScale);
            Assert.Equal(250, applied.MaxScaleMbps);
            Assert.Equal(12, applied.NudgeXPx);
            Assert.Equal(-4, applied.NudgeYPx);
            Assert.Equal(9, applied.OffsetFromTrayPx);
            Assert.False(applied.ShowText);
            Assert.True(applied.StartWithWindows);
            Assert.Equal(FontCatalog.Automatic, applied.FontFamily);

            // The source is untouched: Apply returns a copy, never mutating its input.
            Assert.Equal(750, custom.RefreshIntervalMs);
            Assert.Equal(12, custom.NudgeXPx);
        }
    }

    /// <summary>
    /// Two presets that draw the same widget are one preset and a duplicate button. The
    /// comparison is the one the dialog uses to name the current look, so this also proves
    /// that naming is unambiguous: no config may match two entries.
    /// </summary>
    [Fact]
    public void NoTwoPresetsDrawTheSameWidget()
    {
        for (int i = 0; i < WidgetPresets.All.Count; i++)
        {
            MonitorConfig left = WidgetPresets.Apply(WidgetPresets.All[i], new MonitorConfig());

            for (int j = i + 1; j < WidgetPresets.All.Count; j++)
            {
                MonitorConfig right = WidgetPresets.Apply(WidgetPresets.All[j], new MonitorConfig());

                Assert.False(
                    WidgetPresets.AppearanceEquals(left, right),
                    $"{WidgetPresets.All[i].Name} and {WidgetPresets.All[j].Name} draw the same look");
            }
        }
    }

    /// <summary>
    /// The two colours in a look are what the user reads at a glance, so they must be far
    /// enough apart to tell which row is which. Near-identical pairs are the mistake this
    /// catches, since they pass every other check here.
    /// </summary>
    [Fact]
    public void EveryPresetHasTwoColoursThatCanBeToldApart()
    {
        const double MinimumDistance = 60;

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            double distance = ColorDistance(preset.DownloadColor, preset.UploadColor);
            Assert.True(
                distance >= MinimumDistance,
                $"{preset.Name} uses {preset.DownloadColor} and {preset.UploadColor}, "
                    + $"only {distance:0} apart in RGB");
        }
    }

    /// <summary>
    /// A figure tinted with a pale fill would disappear into the bar it labels, so a preset
    /// that does not name a figure colour gets the bar tint only when it is dark enough to
    /// read as type, and the taskbar's own text colour otherwise.
    /// </summary>
    [Fact]
    public void APresetsFiguresNeverTakeAPaleFillAsTheirColour()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, new MonitorConfig());

            foreach (string mode in new[] { applied.DownloadTextColorMode, applied.UploadTextColorMode })
            {
                Assert.Contains(mode, TextColorModes.All);
            }

            if (preset.DownloadTextMode is null)
            {
                double luminance = WidgetPresets.RelativeLuminance(
                    ColorHelper.Parse(preset.DownloadColor, System.Drawing.Color.White));

                Assert.Equal(
                    luminance <= 0.45 ? TextColorModes.Bar : TextColorModes.Auto,
                    applied.DownloadTextColorMode);
            }
        }
    }

    /// <summary>
    /// The dialog labels the picker from the current settings. A freshly applied preset
    /// must name itself, and a single hand edit must stop it claiming that preset - the
    /// whole point of the label is that it never lies.
    /// </summary>
    [Fact]
    public void TheCurrentLookIsNamedOnlyWhileItStillMatches()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, new MonitorConfig());
            Assert.Same(preset, WidgetPresets.FindByAppearance(applied));

            // One value moved away is enough to stop it being that preset.
            MonitorConfig edited = applied.Clone();
            edited.BarThicknessPx = applied.BarThicknessPx == MonitorConfig.MaxBarThicknessPx
                ? 1
                : applied.BarThicknessPx + 1;

            WidgetPreset? match = WidgetPresets.FindByAppearance(edited);
            Assert.NotSame(preset, match);
        }
    }

    /// <summary>
    /// A preset has to be findable from the settings a real config round-trips through, not
    /// just from the object Apply returned: the dialog reads its controls back into a fresh
    /// config, and that is what gets compared.
    /// </summary>
    [Fact]
    public void TheLookSurvivesTheConfigRoundTrip()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, new MonitorConfig());

            string json = applied.ToJson();
            MonitorConfig reloaded = MonitorConfig.FromJson(json) ?? throw new InvalidOperationException("reload failed");

            Assert.True(
                WidgetPresets.AppearanceEquals(applied, reloaded),
                $"{preset.Name} does not survive a save and reload");

            Assert.Same(preset, WidgetPresets.FindByAppearance(reloaded));
        }
    }

    /// <summary>
    /// The first entry is the look a fresh install starts with, so a new config must name
    /// it rather than showing "Custom" on first run.
    /// </summary>
    [Fact]
    public void AFreshInstallAlreadyMatchesTheFirstPreset()
    {
        MonitorConfig fresh = new MonitorConfig().Normalize();
        WidgetPreset? match = WidgetPresets.FindByAppearance(fresh);
        Assert.NotNull(match);
        Assert.Equal(WidgetPresets.All[0].Name, match!.Name);
    }

    /// <summary>
    /// A preset that changes the colour of only one row would be a half-applied look, and
    /// the "same widget" comparison would then depend on which row was edited last.
    /// </summary>
    [Fact]
    public void EveryPresetDecidesTheColourOfBothRows()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig applied = WidgetPresets.Apply(preset, new MonitorConfig());

            Assert.Contains(applied.DownloadTextColorMode, TextColorModes.All);
            Assert.Contains(applied.UploadTextColorMode, TextColorModes.All);

            // A custom mode must carry a colour the user can actually be shown.
            if (applied.DownloadTextColorMode == TextColorModes.Custom)
            {
                Assert.Matches("^#[0-9A-F]{6}$", applied.DownloadTextColorHex);
            }

            if (applied.UploadTextColorMode == TextColorModes.Custom)
            {
                Assert.Matches("^#[0-9A-F]{6}$", applied.UploadTextColorHex);
            }
        }
    }

    /// <summary>
    /// The gallery is presented in the dialog, so it has to reach it: the picker is filled
    /// from the same list these tests walk.
    /// </summary>
    [Fact]
    public void TheDialogFillsItsPickerFromThisGallery()
    {
        string source = SourceText.Read("UI", "SettingsForm.cs");

        Assert.Contains("WidgetPresets.All", source, StringComparison.Ordinal);
        Assert.Contains("WidgetPresets.Apply(", source, StringComparison.Ordinal);
        Assert.Contains("WidgetPresets.FindByAppearance(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A preset is a promise about pixels, not about fields: two entries that differ only in
    /// a value the renderer ignores would still pass a field comparison. This renders every
    /// preset through the real renderer and requires a distinct picture, which is the
    /// property the gallery in the dialog is claiming.
    /// </summary>
    [Fact]
    public void EveryPresetRendersADistinctPicture()
    {
        var renderer = new HASNetMonitor.Rendering.WidgetRenderer();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        const double bytesPerMegabit = 1_000_000 / 8.0;
        const double downMbps = 86.4;
        const double upMbps = 21.7;

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig config = WidgetPresets.Apply(preset, new MonitorConfig()).Normalize();
            HASNetMonitor.Rendering.WidgetLayout layout = renderer.ComputeLayout(config, 4f, 0);

            double peak = Math.Max(config.MaxScaleMbps, 0.01) * bytesPerMegabit;
            double downRatio = Math.Clamp((downMbps * bytesPerMegabit) / peak, 0, 1);
            double upRatio = Math.Clamp((upMbps * bytesPerMegabit) / peak, 0, 1);

            string signature;
            using (var frame = renderer.Render(
                new HASNetMonitor.Rendering.WidgetVisual(
                    downMbps * bytesPerMegabit, upMbps * bytesPerMegabit, downRatio, upRatio),
                config, layout, WidgetTheme.Dark,
                TaskbarOrientation.Horizontal))
            {
                // A cheap, stable fingerprint: the size plus every pixel's channel sum. Two
                // looks that differ anywhere in colour or shape differ here.
                var sum = new System.Text.StringBuilder();
                sum.Append(frame.Width).Append('x').Append(frame.Height).Append(':');

                long total = 0;
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        System.Drawing.Color pixel = frame.GetPixel(x, y);
                        total = (total * 31) + (pixel.R * 3) + (pixel.G * 5) + (pixel.B * 7) + pixel.A;
                    }
                }

                sum.Append(total);
                signature = sum.ToString();
            }

            Assert.False(
                seen.TryGetValue(signature, out string? other),
                $"{preset.Name} renders the same picture as {other}");

            seen[signature] = preset.Name;
        }
    }

    /// <summary>
    /// The taskbar is either near-black or near-white and the app follows whichever the user
    /// has. An opaque fill has no lightness that stands out on both, so a very dark bar
    /// disappears on a dark taskbar and a very pale one on a light taskbar. Every preset
    /// must therefore keep both of its rows discernible against either.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryPresetStaysVisibleOnEitherTaskbar(bool darkTaskbar)
    {
        // The colours WidgetTheme paints behind the bars, from its constructor values.
        System.Drawing.Color background = darkTaskbar
            ? System.Drawing.Color.FromArgb(30, 30, 30)
            : System.Drawing.Color.FromArgb(249, 249, 249);

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            Assert.True(
                Contrast(preset.DownloadColor, background) >= MinimumTaskbarContrast,
                $"{preset.Name} download {preset.DownloadColor} is too close to a "
                    + (darkTaskbar ? "dark" : "light") + " taskbar");

            Assert.True(
                Contrast(preset.UploadColor, background) >= MinimumTaskbarContrast,
                $"{preset.Name} upload {preset.UploadColor} is too close to a "
                    + (darkTaskbar ? "dark" : "light") + " taskbar");
        }
    }

    /// <summary>
    /// A preset is applied to an existing configuration, so the values it does not mention
    /// must survive. Losing a user's position, clock settings or peak scale by clicking a
    /// colour scheme would be a serious accident.
    /// </summary>
    [Fact]
    public void ApplyingAPresetLeavesEverythingItDoesNotStyleAlone()
    {
        var before = new MonitorConfig
        {
            OffsetFromTrayPx = 21,
            NudgeXPx = -7,
            NudgeYPx = 4,
            RefreshIntervalMs = 1500,
            ShowText = false,
            MaxScaleMbps = 42.5,
        }.Normalize();

        MonitorConfig after = WidgetPresets.Apply(WidgetPresets.All[7], before);

        Assert.Equal(before.OffsetFromTrayPx, after.OffsetFromTrayPx);
        Assert.Equal(before.NudgeXPx, after.NudgeXPx);
        Assert.Equal(before.NudgeYPx, after.NudgeYPx);
        Assert.Equal(before.RefreshIntervalMs, after.RefreshIntervalMs);
        Assert.Equal(before.ShowText, after.ShowText);
        Assert.Equal(before.MaxScaleMbps, after.MaxScaleMbps);
    }

    /// <summary>WCAG relative-contrast ratio between a hex colour and a background.</summary>
    /// <summary>
    /// The visibility rule is about the pixels a user sees, so it is also checked through
    /// the renderer on a light taskbar: a pale preset must not paint bars that melt into
    /// the background, and the widget must still show both rows as bright as the dark one.
    /// </summary>
    [Fact]
    public void PalePresetsStillPaintBarsOnALightTaskbar()
    {
        var renderer = new HASNetMonitor.Rendering.WidgetRenderer();
        var light = new HASNetMonitor.Rendering.WidgetTheme(false);

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig config = WidgetPresets.Apply(preset, new MonitorConfig()).Normalize();
            HASNetMonitor.Rendering.WidgetLayout layout = renderer.ComputeLayout(config, 4f, 0);

            using var frame = renderer.Render(
                new HASNetMonitor.Rendering.WidgetVisual(10_000_000, 2_000_000, 0.5, 0.25),
                config, layout, light, TaskbarOrientation.Horizontal);

            // The light taskbar sits behind an opaque frame only when a backdrop is on;
            // otherwise the frame's own pixels are the widget. Count the pixels that carry
            // each row's colour anywhere in the bar area.
            int download = 0;
            int upload = 0;
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    System.Drawing.Color pixel = frame.GetPixel(x, y);
                    if (CloseTo(pixel, ParseColor(preset.DownloadColor)))
                    {
                        download++;
                    }

                    if (CloseTo(pixel, ParseColor(preset.UploadColor)))
                    {
                        upload++;
                    }
                }
            }

            Assert.True(download > 30, $"{preset.Name} download bar is not painted on a light taskbar");
            Assert.True(upload > 30, $"{preset.Name} upload bar is not painted on a light taskbar");
        }
    }

    private static bool CloseTo(System.Drawing.Color left, System.Drawing.Color right)
        => Math.Abs(left.R - right.R) <= 12
            && Math.Abs(left.G - right.G) <= 12
            && Math.Abs(left.B - right.B) <= 12;

    /// <summary>
    /// A gallery that never shows a style is a gallery with a gap: the presets are the
    /// discoverable surface, so every style and every arrow the app offers must appear in
    /// at least one preset.
    /// </summary>
    [Fact]
    public void ThePresetsCoverEveryStyleAndArrow()
    {
        var styles = new HashSet<string>(WidgetPresets.All.Select(p => p.Style), StringComparer.Ordinal);
        var arrows = new HashSet<string>(WidgetPresets.All.Select(p => p.ArrowStyle), StringComparer.Ordinal);

        foreach (string style in WidgetStyles.All)
        {
            Assert.Contains(style, styles);
        }

        foreach (string arrow in ArrowStyles.All)
        {
            Assert.Contains(arrow, arrows);
        }
    }

    private static double Contrast(string hex, System.Drawing.Color background)
    {
        double first = RelativeLuminance(ParseColor(hex));
        double second = RelativeLuminance(background);
        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(System.Drawing.Color color)
    {
        static double Channel(int value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static System.Drawing.Color ParseColor(string hex)
        => System.Drawing.Color.FromArgb(
            Convert.ToInt32(hex.Substring(1, 2), 16),
            Convert.ToInt32(hex.Substring(3, 2), 16),
            Convert.ToInt32(hex.Substring(5, 2), 16));

    /// <summary>
    /// Below this a fill is not distinguishable from the taskbar behind it. It is a
    /// visibility floor, not a text-contrast requirement: bars are large shapes, not glyphs.
    /// </summary>
    private const double MinimumTaskbarContrast = 1.75;

    private static double ColorDistance(string first, string second)
    {
        System.Drawing.Color a = ColorHelper.Parse(first, System.Drawing.Color.Black);
        System.Drawing.Color b = ColorHelper.Parse(second, System.Drawing.Color.Black);

        double dr = a.R - b.R;
        double dg = a.G - b.G;
        double db = a.B - b.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }
}
