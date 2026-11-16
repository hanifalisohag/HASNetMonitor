using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The preset picker as the user meets it: a control in the dialog that, when chosen,
/// changes the other controls and the result the dialog returns. These drive the real
/// form on an STA thread rather than the preset table directly, because the part that can
/// go wrong is the wiring - a preset that applies in Core but never reaches the controls
/// would look fine in every other test and do nothing on screen.
/// </summary>
public sealed class SettingsPresetTests
{
    /// <summary>
    /// Choosing a preset must change what OK returns. The result is rebuilt from the
    /// controls, so this fails if the preset only updates the note or the picker.
    /// </summary>
    [Fact]
    public void ChoosingAPresetChangesWhatTheDialogReturns()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig result = RunOnUiThread(() =>
            {
                using var form = new SettingsForm(new MonitorConfig());

                // Start from a look that is definitely not this one, so a no-op handler
                // cannot pass the test by luck.
                MonitorConfig different = WidgetPresets.Apply(
                    WidgetPresets.All.First(p => !string.Equals(p.Name, preset.Name, StringComparison.Ordinal)),
                    new MonitorConfig());
                form.FillForTest(different);

                form.ChoosePresetForTest(preset.Name);
                return form.BuildResult();
            });

            MonitorConfig expected = WidgetPresets.Apply(preset, new MonitorConfig());

            Assert.Equal(expected.Style, result.Style);
            Assert.Equal(expected.ArrowStyle, result.ArrowStyle);
            Assert.Equal(expected.SortOrder, result.SortOrder);
            Assert.Equal(expected.DownloadColorHex, result.DownloadColorHex);
            Assert.Equal(expected.UploadColorHex, result.UploadColorHex);
            Assert.Equal(expected.BarThicknessPx, result.BarThicknessPx);
            Assert.Equal(expected.SegmentGapPx, result.SegmentGapPx);
            Assert.Equal(expected.FontSizePt, result.FontSizePt, 3);
            Assert.Equal(expected.FontBold, result.FontBold);
            Assert.Equal(expected.RoundedBars, result.RoundedBars);
            Assert.Equal(expected.AccentGlow, result.AccentGlow);
            Assert.Equal(expected.ShowBackdrop, result.ShowBackdrop);
            Assert.Equal(expected.CrispText, result.CrispText);
            Assert.Equal(expected.DownloadTextColorMode, result.DownloadTextColorMode);
            Assert.Equal(expected.UploadTextColorMode, result.UploadTextColorMode);
        }
    }

    /// <summary>
    /// The controls must show the preset too. If only the returned config changed, the user
    /// would see one thing in the dialog and get another in the widget.
    /// </summary>
    [Fact]
    public void TheControlsShowThePresetThatWasChosen()
    {
        WidgetPreset preset = WidgetPresets.Find("Terminal")!;

        (string Style, bool Backdrop, bool Crisp, int Thickness, string Download) = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            form.ChoosePresetForTest(preset.Name);
            return (form.StyleForTest, form.BackdropForTest, form.CrispForTest,
                    form.BarThicknessForTest, form.DownloadColorForTest);
        });

        Assert.Equal(preset.Style, Style);
        Assert.Equal(preset.BarThicknessPx, Thickness);
        Assert.Equal(preset.DownloadColor, Download);

        // Terminal is the crisp preset, so the on/off controls have to agree with it.
        Assert.True(Crisp);
        Assert.True(Backdrop);
    }

    /// <summary>
    /// Crisp text forces an opaque plate. The controls must settle on that, not on the
    /// preset's own opacity, or the dialog would show a translucent plate the renderer
    /// then draws solid.
    /// </summary>
    [Fact]
    public void ACrispPresetLeavesThePlateControlsConsistent()
    {
        WidgetPreset preset = WidgetPresets.All.First(p => p.CrispText);

        MonitorConfig result = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            form.ChoosePresetForTest(preset.Name);
            return form.BuildResult();
        });

        Assert.True(result.CrispText);
        Assert.True(result.ShowBackdrop, "crisp text without a plate is not drawable");
        Assert.Equal(255, result.BackdropOpacity);
    }

    /// <summary>
    /// Then editing a single control must drop the picker back to Custom: the label is a
    /// claim about the current settings, and after a hand edit that claim is wrong.
    /// </summary>
    [Fact]
    public void HandEditingAfterAPresetShowsCustom()
    {
        WidgetPreset preset = WidgetPresets.Find("Ember")!;

        (string? before, string? after) = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            form.ChoosePresetForTest(preset.Name);
            string? beforeEdit = form.SelectedPresetForTest;

            form.SetBarThicknessForTest(preset.BarThicknessPx == MonitorConfig.MaxBarThicknessPx ? 1 : preset.BarThicknessPx + 1);
            return (beforeEdit, form.SelectedPresetForTest);
        });

        Assert.Equal(preset.Name, before);
        Assert.Null(after);
    }

    /// <summary>
    /// Opening the dialog on existing settings must name the preset they happen to match,
    /// so a user who chose a preset last time sees that name rather than Custom.
    /// </summary>
    [Fact]
    public void TheDialogNamesThePresetItsSettingsMatchOnOpen()
    {
        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            MonitorConfig stored = WidgetPresets.Apply(preset, new MonitorConfig());

            string? shown = RunOnUiThread(() =>
            {
                using var form = new SettingsForm(stored.Clone());
                return form.SelectedPresetForTest;
            });

            Assert.Equal(preset.Name, shown);
        }
    }

    /// <summary>
    /// A config saved before the gallery existed matches nothing, so the picker must open
    /// on Custom and say so rather than picking an arbitrary entry.
    /// </summary>
    [Fact]
    public void EditedSettingsOpenAsCustom()
    {
        MonitorConfig edited = WidgetPresets.Apply(WidgetPresets.All[3], new MonitorConfig());
        edited.BarThicknessPx = edited.BarThicknessPx == 1 ? 2 : edited.BarThicknessPx - 1;
        edited.Normalize();

        string? shown = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(edited.Clone());
            return form.SelectedPresetForTest;
        });

        Assert.Null(shown);
    }

    /// <summary>
    /// Applying a preset must not disturb anything the preset does not own: the dialog is
    /// the last thing between the user's settings and the stored config.
    /// </summary>
    [Fact]
    public void ChoosingAPresetKeepsPositionAndCadence()
    {
        MonitorConfig original = new MonitorConfig
        {
            RefreshIntervalMs = 1250,
            NudgeXPx = 7,
            NudgeYPx = -3,
            OffsetFromTrayPx = 11,
            AutoScale = true,
            MaxScaleMbps = 400,
            ShowText = false,
            StartWithWindows = true,
        }.Normalize();

        MonitorConfig result = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(original.Clone());
            form.ChoosePresetForTest(WidgetPresets.Find("Nebula")!.Name);
            return form.BuildResult();
        });

        Assert.Equal(1250, result.RefreshIntervalMs);
        Assert.Equal(7, result.NudgeXPx);
        Assert.Equal(-3, result.NudgeYPx);
        Assert.Equal(11, result.OffsetFromTrayPx);
        Assert.True(result.AutoScale);
        Assert.Equal(400, result.MaxScaleMbps);
        Assert.False(result.ShowText);
        Assert.True(result.StartWithWindows);
    }

    /// <summary>
    /// The picker has one entry per preset plus the Custom state, and opens without a
    /// layout error on every one.
    /// </summary>
    [Fact]
    public void ThePickerListsEveryPreset()
    {
        int entries = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            return form.PresetCountForTest;
        });

        Assert.Equal(WidgetPresets.All.Count, entries);
    }

    /// <summary>
    /// The presets row is extra height on a fixed-height dialog. A netbook screen is 768 px
    /// tall, so the dialog has to stay well inside it or the OK button is off screen and the
    /// user cannot commit a preset they just picked.
    /// </summary>
    [Fact]
    public void TheDialogFitsOnASmallScreen()
    {
        using var form = RunOnUiThread(() =>
        {
            var built = new SettingsForm(new MonitorConfig());
            built.PerformLayout();
            return built;
        });

        // CreateControl forces the handles so the auto-sized layout is final.
        RunOnUiThread(() =>
        {
            form.CreateControl();
            return form.PreferredSize.Height;
        });

        Assert.True(form.Width <= 1280, $"dialog is {form.Width} px wide");

        // A 768 px netbook screen leaves ~740 px usable once the taskbar and title bar are
        // accounted for; anything above that hides the OK button.
        Assert.True(form.Height <= 740, $"dialog is {form.Height} px tall, which will not fit 768 px");
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
}
