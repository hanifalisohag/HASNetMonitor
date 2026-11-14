namespace TaskbarNetMonitor.Tests;

/// <summary>
/// The config file, on whichever backend this target uses: System.Text.Json for
/// <c>net11.0-windows</c>, the in-box JavaScriptSerializer plus a comment/trailing-comma
/// pre-pass for <c>net48</c>. The whole suite runs on both targets, so these tests hold
/// each backend to the same contract, and the file on disk stays interchangeable.
/// </summary>
public class ConfigJsonParityTests
{
    /// <summary>Every persisted setting given a value that differs from the default.</summary>
    private static MonitorConfig Populated() => new()
    {
        DownloadColorHex = "#123456",
        UploadColorHex = "#ABCDEF",
        RefreshIntervalMs = 500,
        OffsetFromTrayPx = 12,
        NudgeXPx = -24,
        NudgeYPx = 7,
        AutoScale = false,
        MaxScaleMbps = 250.5,
        ShowText = false,
        ShowBackdrop = true,
        BackdropOpacity = 90,
        BarThicknessPx = 7,
        SegmentGapPx = 5,
        FontSizePt = 11.5,
        Style = WidgetStyles.Dots,
        SortOrder = StackOrder.UploadFirst,
        FontBold = true,
        FontItalic = true,
        RoundedBars = true,
        AccentGlow = true,
        StartWithWindows = false,
    };

    [Fact]
    public void EverySettingSurvivesTheRoundTrip()
    {
        MonitorConfig original = Populated();
        MonitorConfig parsed = MonitorConfig.FromJson(original.ToJson());

        // Compare through the serialised form so a property added later is covered
        // without editing this test.
        Assert.Equal(original.ToJson(), parsed.ToJson());

        Assert.Equal("#123456", parsed.DownloadColorHex);
        Assert.Equal("#ABCDEF", parsed.UploadColorHex);
        Assert.Equal(500, parsed.RefreshIntervalMs);
        Assert.Equal(12, parsed.OffsetFromTrayPx);
        Assert.Equal(-24, parsed.NudgeXPx);
        Assert.Equal(7, parsed.NudgeYPx);
        Assert.False(parsed.AutoScale);
        Assert.Equal(250.5, parsed.MaxScaleMbps);
        Assert.False(parsed.ShowText);
        Assert.True(parsed.ShowBackdrop);
        Assert.Equal(90, parsed.BackdropOpacity);
        Assert.Equal(7, parsed.BarThicknessPx);
        Assert.Equal(5, parsed.SegmentGapPx);
        Assert.Equal(11.5, parsed.FontSizePt);
        Assert.Equal(WidgetStyles.Dots, parsed.Style);
        Assert.Equal(StackOrder.UploadFirst, parsed.SortOrder);
        Assert.True(parsed.FontBold);
        Assert.True(parsed.FontItalic);
        Assert.True(parsed.RoundedBars);
        Assert.True(parsed.AccentGlow);
        Assert.False(parsed.StartWithWindows);
    }

    [Fact]
    public void SerialisingTwiceGivesTheSameText()
    {
        MonitorConfig config = Populated();

        Assert.Equal(config.ToJson(), config.ToJson());
        Assert.Equal(config.ToJson(), MonitorConfig.FromJson(config.ToJson()).ToJson());
    }

    [Fact]
    public void BothCommentStylesAreIgnored()
    {
        const string json = """
            {
              // a line comment
              "RefreshIntervalMs": 750, /* and a block one */
              "Style": "Neon" // trailing line comment
            }
            """;

        MonitorConfig parsed = MonitorConfig.FromJson(json);

        Assert.Equal(750, parsed.RefreshIntervalMs);
        Assert.Equal(WidgetStyles.Neon, parsed.Style);
    }

    [Fact]
    public void ACommentMarkerInsideAStringIsKept()
    {
        // The net48 pre-pass strips comments textually, before any parser sees the
        // text, so a string value that merely looks like a comment must survive it
        // untouched. This goes straight to the JSON layer: MonitorConfig.FromJson
        // normalises afterwards, and normalisation has its own tests.
        const string json = """{ "FontFamily": "A//B", "Style": "C/*D*/E" }""";

        MonitorConfig parsed = ConfigJson.Deserialize(json);

        Assert.Equal("A//B", parsed.FontFamily);
        Assert.Equal("C/*D*/E", parsed.Style);
    }

    [Fact]
    public void ATrailingCommaIsAccepted()
    {
        const string json = """
            {
              "RefreshIntervalMs": 400,
              "Style": "Minimal",
            }
            """;

        MonitorConfig parsed = MonitorConfig.FromJson(json);

        Assert.Equal(400, parsed.RefreshIntervalMs);
        Assert.Equal(WidgetStyles.Minimal, parsed.Style);
    }

    [Fact]
    public void PropertyNamesAreCaseInsensitive()
    {
        const string json = """{ "refreshintervalms": 350, "STYLE": "Bars" }""";

        MonitorConfig parsed = MonitorConfig.FromJson(json);

        Assert.Equal(350, parsed.RefreshIntervalMs);
        Assert.Equal(WidgetStyles.Bars, parsed.Style);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        const string json = """{ "FromAnOlderVersion": "x", "RefreshIntervalMs": 300 }""";

        Assert.Equal(300, MonitorConfig.FromJson(json).RefreshIntervalMs);
    }

    [Fact]
    public void BrokenJsonFallsBackToDefaults()
    {
        foreach (string broken in new[] { "{ not json", "{{{", "\"text\"", "{ \"RefreshIntervalMs\": }" })
        {
            MonitorConfig parsed = MonitorConfig.FromJson(broken);

            Assert.Equal(MonitorConfig.DefaultRefreshIntervalMs, parsed.RefreshIntervalMs);
            Assert.Equal(WidgetStyles.Bars, parsed.Style);
        }
    }

    [Fact]
    public void EmptyInputGivesDefaults()
    {
        Assert.Equal(MonitorConfig.DefaultRefreshIntervalMs, MonitorConfig.FromJson(null).RefreshIntervalMs);
        Assert.Equal(MonitorConfig.DefaultRefreshIntervalMs, MonitorConfig.FromJson("").Normalize().RefreshIntervalMs);
        Assert.Equal(MonitorConfig.DefaultRefreshIntervalMs, MonitorConfig.FromJson("   ").RefreshIntervalMs);
    }

    [Fact]
    public void OutOfRangeValuesAreRepairedOnLoad()
    {
        const string json = """
            { "RefreshIntervalMs": 999999, "BarThicknessPx": 500, "FontSizePt": 400, "Style": "NotAStyle" }
            """;

        MonitorConfig parsed = MonitorConfig.FromJson(json).Normalize();

        Assert.InRange(parsed.RefreshIntervalMs, MonitorConfig.MinRefreshIntervalMs, MonitorConfig.MaxRefreshIntervalMs);
        Assert.InRange(parsed.BarThicknessPx, 1, MonitorConfig.MaxBarThicknessPx);
        Assert.InRange(parsed.FontSizePt, MonitorConfig.MinFontSizePt, MonitorConfig.MaxFontSizePt);
        Assert.Equal(WidgetStyles.Bars, parsed.Style);
    }

    [Fact]
    public void AConfigWrittenOnOneBackendReadsBackTheSame()
    {
        // Both targets write the same keys, so text produced by either parses here.
        string text = Populated().ToJson();

        Assert.Contains("\"RefreshIntervalMs\"", text, StringComparison.Ordinal);
        Assert.Contains("500", text, StringComparison.Ordinal);

        MonitorConfig parsed = MonitorConfig.FromJson(text);
        Assert.Equal(500, parsed.RefreshIntervalMs);
    }

    [Fact]
    public void NumbersAreWrittenInvariantly()
    {
        // A comma decimal separator on the user's machine must not leak into the file.
        MonitorConfig config = Populated();
        config.MaxScaleMbps = 12.5;

        string text = config.ToJson();

        Assert.Contains("12.5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("12,5", text, StringComparison.Ordinal);
        Assert.Equal(12.5, MonitorConfig.FromJson(text).MaxScaleMbps, 6);
    }

    [Fact]
    public void ConcurrentReadersAndWritersStayConsistent()
    {
        // The settings dialog and the widget can touch the config at the same time, and
        // xunit runs these classes in parallel. Both backends must survive that: same
        // text out, no lost values, no exception. The net48 backend needs its lock for
        // this, because JavaScriptSerializer is documented as not thread safe.
        string expected = Populated().ToJson();
        var problems = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 16, worker =>
        {
            for (int round = 0; round < 40; round++)
            {
                try
                {
                    string text = Populated().ToJson();
                    if (!string.Equals(text, expected, StringComparison.Ordinal))
                    {
                        problems.Add($"worker {worker} serialised differently");
                    }

                    MonitorConfig parsed = MonitorConfig.FromJson(expected);
                    if (!string.Equals(parsed.ToJson(), expected, StringComparison.Ordinal))
                    {
                        problems.Add($"worker {worker} round-trip differed");
                    }

                    if (parsed.RefreshIntervalMs != 500 || parsed.MaxScaleMbps != 250.5)
                    {
                        problems.Add($"worker {worker} lost a value");
                    }
                }
                catch (Exception ex)
                {
                    problems.Add($"worker {worker} threw {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        Assert.Empty(problems);
    }

    [Fact]
    public void TheHelpersMatchTheFrameworkBehaviourTheyReplace()
    {
        // Numeric.Clamp and Numeric.IsFinite stand in for Math.Clamp and
        // double.IsFinite, which .NET Framework does not have.
        Assert.Equal(5, Numeric.Clamp(5, 0, 10));
        Assert.Equal(0, Numeric.Clamp(-1, 0, 10));
        Assert.Equal(10, Numeric.Clamp(99, 0, 10));

        Assert.Equal(0.25, Numeric.Clamp(0.25, 0, 1));
        Assert.Equal(1.0, Numeric.Clamp(9.9, 0, 1));
        Assert.Equal(0.5f, Numeric.Clamp(0.5f, 0, 1));
        Assert.Equal(3m, Numeric.Clamp(3m, 0m, 5m));

        // NaN passes through untouched, as Math.Clamp does.
        Assert.True(double.IsNaN(Numeric.Clamp(double.NaN, 0, 1)));

        Assert.True(Numeric.IsFinite(0));
        Assert.True(Numeric.IsFinite(-1234.5));
        Assert.False(Numeric.IsFinite(double.NaN));
        Assert.False(Numeric.IsFinite(double.PositiveInfinity));
        Assert.False(Numeric.IsFinite(double.NegativeInfinity));
    }

    [Fact]
    public void ColourTextSurvivesTheJsonLayerAsWritten()
    {
        // The JSON layer must not rewrite colour text; converting it to a canonical
        // #RRGGBB form is Normalize's job, which is tested separately.
        foreach (string hex in new[] { "#2ECC71", "#abcdef", "2ECC71" })
        {
            MonitorConfig parsed = ConfigJson.Deserialize("{ \"DownloadColorHex\": \"" + hex + "\" }");

            Assert.Equal(hex, parsed.DownloadColorHex);
        }
    }
}
