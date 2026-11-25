using System.Drawing.Imaging;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

// Renders the widget frames the README gallery is composed from, using the app's own
// renderer so the previews are real output and not mockups. It asserts nothing and only
// writes PNGs when GALLERY_OUT is set; tools/make_previews.py turns them into imgs/*.png.
//
//   GALLERY_OUT=<abs path> dotnet test --filter FullyQualifiedName~PreviewFrames
//   python tools/make_previews.py
public class PreviewFrames
{
    [Fact]
    public void Render()
    {
        string? outDir = Environment.GetEnvironmentVariable("GALLERY_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);

        var renderer = new WidgetRenderer();

        void Shot(string name, MonitorConfig config, WidgetTheme theme, float scale, float down = 86.4f, float up = 21.7f)
        {
            MonitorConfig cfg = config.Normalize();

            // A fixed reading so the chip previews show real levels rather than placeholders.
            var stats = new SystemStatsReading(
                42.0, 63.0, 27.0, 214_000_000_000,
                new DiskMonitor.DiskReading("C:", 214_000_000_000, 512_000_000_000) { ActivityPercent = 58.0 },
                new BatteryMonitor.BatteryReading(true, 76, false));
            WidgetLayout layout = renderer.ComputeLayout(cfg, scale, 0, stats);

            // The renderer fills bars from the ratios, not from the byte counts. The app gets
            // these from AutoScaler; a fixed manual peak keeps the gallery frames reproducible.
            const double bytesPerMegabit = 1_000_000 / 8.0;
            double peak = Math.Max(cfg.MaxScaleMbps, 0.01) * bytesPerMegabit;
            double downRatio = Math.Clamp((down * bytesPerMegabit) / peak, 0, 1);
            double upRatio = Math.Clamp((up * bytesPerMegabit) / peak, 0, 1);

            using Bitmap frame = renderer.Render(
                new WidgetVisual(down * bytesPerMegabit, up * bytesPerMegabit, downRatio, upRatio, stats),
                cfg, layout, theme, TaskbarOrientation.Horizontal);
            frame.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        }

        MonitorConfig Base(string style, string arrow, string textMode, string down = "#4EA1FF", string up = "#FF9F45") =>
            new MonitorConfig
            {
                Style = style,
                ArrowStyle = arrow,
                DownloadColorHex = down,
                UploadColorHex = up,
                DownloadTextColorMode = textMode,
                UploadTextColorMode = textMode,
                ShowText = true,
            };

        WidgetTheme light = WidgetTheme.Light;

        Shot("card_Bars", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("card_Sidebar", Base(WidgetStyles.Sidebar, ArrowStyles.Solid, TextColorModes.Bar), WidgetTheme.Dark, 4f);
        Shot("card_Segments", Base(WidgetStyles.Segments, ArrowStyles.Chevron, TextColorModes.Custom, down: "#38D39F", up: "#C77DFF"), WidgetTheme.Dark, 4f);
        Shot("card_Minimal", Base(WidgetStyles.Minimal, ArrowStyles.None, TextColorModes.Bar, down: "#8AB4F8"), WidgetTheme.Dark, 4f);
        Shot("card_Neon", Base(WidgetStyles.Neon, ArrowStyles.Double, TextColorModes.Custom, down: "#FFD166", up: "#06D6A0"), WidgetTheme.Dark, 4f);
        Shot("card_Dots", Base(WidgetStyles.Dots, ArrowStyles.Small, TextColorModes.Bar, down: "#5BC0BE", up: "#F25F5C"), WidgetTheme.Dark, 4f);
        MonitorConfig glassCard = Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto, down: "#7FD1FF", up: "#FFB454");
        glassCard.GlassMeters = true;
        Shot("card_Glass", glassCard, WidgetTheme.Dark, 4f);

        // The chips: their own colour each, a level ring, and vector icons.
        MonitorConfig ringChips = Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto);
        ringChips.ShowCpu = true;
        ringChips.ShowRam = true;
        ringChips.ShowGpu = true;
        ringChips.ShowDisk = true;
        ringChips.ChipIndicator = ChipIndicators.Ring;
        ringChips.ChipIcon = ChipIcons.Glyph;
        ringChips.CpuColorHex = "#FF8A65";
        ringChips.RamColorHex = "#4DD0E1";
        ringChips.GpuColorHex = "#B388FF";
        ringChips.DiskColorHex = "#9CCC65";
        Shot("chips_ring", ringChips, WidgetTheme.Dark, 4.5f);

        MonitorConfig glassChips = Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto);
        glassChips.ShowCpu = true;
        glassChips.ShowRam = true;
        glassChips.ShowGpu = true;
        glassChips.ChipIndicator = ChipIndicators.Bar;
        glassChips.ChipIcon = ChipIcons.Glyph;
        glassChips.GlassMeters = true;
        glassChips.GlassChips = true;
        Shot("chips_glass", glassChips, WidgetTheme.Dark, 4.5f);

        // The README's chip preview: all four chips (CPU, GPU, RAM, disk) with a bar level.
        MonitorConfig barChips = Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto);
        barChips.ShowCpu = true;
        barChips.ShowRam = true;
        barChips.ShowGpu = true;
        barChips.ShowDisk = true;
        barChips.ChipIndicator = ChipIndicators.Bar;
        barChips.ChipIcon = ChipIcons.Glyph;
        barChips.CpuColorHex = "#FF8A65";
        barChips.RamColorHex = "#4DD0E1";
        barChips.GpuColorHex = "#B388FF";
        barChips.DiskColorHex = "#9CCC65";
        Shot("chips_bar", barChips, WidgetTheme.Dark, 4.5f);

        Shot("arrow_Bar", Base(WidgetStyles.Bars, ArrowStyles.Bar, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_Classic", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_Double", Base(WidgetStyles.Bars, ArrowStyles.Double, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_Chevron", Base(WidgetStyles.Bars, ArrowStyles.Chevron, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_Solid", Base(WidgetStyles.Bars, ArrowStyles.Solid, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_Small", Base(WidgetStyles.Bars, ArrowStyles.Small, TextColorModes.Auto), WidgetTheme.Dark, 4f);
        Shot("arrow_None", Base(WidgetStyles.Bars, ArrowStyles.None, TextColorModes.Auto), WidgetTheme.Dark, 4f);

        Shot("text_white", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto), WidgetTheme.Dark, 4.5f);
        Shot("text_bar", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Bar, down: "#4EA1FF", up: "#FF9F45"), WidgetTheme.Dark, 4.5f);
        Shot("text_accent", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Custom, down: "#38D39F", up: "#C77DFF"), WidgetTheme.Dark, 4.5f);
        Shot("text_contrast", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Custom, down: "#FFD166", up: "#FF6B6B"), WidgetTheme.Dark, 4.5f);

        Shot("theme_dark", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto), WidgetTheme.Dark, 6.5f);
        Shot("theme_light", Base(WidgetStyles.Bars, ArrowStyles.Classic, TextColorModes.Auto), light, 6.5f);

        // The hero shows the shipped look with every chip on: the config below is the one the
        // README's first image is meant to advertise, parsed through the app's own reader so
        // the preview cannot drift from what that JSON actually renders. Two frames are
        // rendered - the letter markers and the icon markers - so the hero can show both.
        Shot("hero", MonitorConfig.FromJson(HeroJson(ChipIcons.Detailed)), WidgetTheme.Dark, 7f, 92f, 26f);
        Shot("hero_letters", MonitorConfig.FromJson(HeroJson(ChipIcons.Letter)), WidgetTheme.Dark, 7f, 92f, 26f);
    }

    // The look the README's hero advertises: segmented side bars, upload first, all four
    // chips with a bar level and vector icon markers, each chip's text following its colour.
    // The marker style is the one thing the two hero frames differ in.
    private static string HeroJson(string chipIcon) => $$"""
        {
          "DownloadColorHex": "#2ECC71",
          "UploadColorHex": "#E6643C",
          "DownloadTextColorMode": "Bar",
          "UploadTextColorMode": "Bar",
          "DownloadTextColorHex": "#2ECC71",
          "UploadTextColorHex": "#E6643C",
          "ArrowStyle": "Small",
          "RefreshIntervalMs": 1000,
          "OffsetFromTrayPx": 6,
          "NudgeXPx": 0,
          "NudgeYPx": 0,
          "AutoScale": true,
          "MaxScaleMbps": 100,
          "ShowText": true,
          "ShowBackdrop": false,
          "BackdropOpacity": 255,
          "BarThicknessPx": 3,
          "SegmentGapPx": 1,
          "FontSizePt": 10.5,
          "Style": "SidebarSegments",
          "SortOrder": "UploadFirst",
          "FontFamily": "",
          "FontBold": true,
          "FontItalic": false,
          "RoundedBars": true,
          "SharedScale": true,
          "AccentGlow": false,
          "ArrowSizePercent": 150,
          "CrispText": false,
          "StartWithWindows": true,
          "ShowCpu": true,
          "ShowGpu": true,
          "ShowRam": true,
          "ShowDisk": true,
          "ShowBattery": false,
          "CpuColorHex": "",
          "RamColorHex": "",
          "GpuColorHex": "",
          "DiskColorHex": "",
          "BatteryColorHex": "",
          "CpuTextColorHex": "",
          "RamTextColorHex": "",
          "GpuTextColorHex": "",
          "DiskTextColorHex": "",
          "BatteryTextColorHex": "",
          "ChipIndicator": "Bar",
          "ChipIcon": "{{chipIcon}}",
          "GlassMeters": false,
          "GlassChips": false,
          "ChipTextFollowsChip": true
        }
        """;
}
