using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// Two things the settings dialog must not get wrong again: the Position group is tall
/// enough that it used to fall below the form's visible area and cut the "Reset nudges"
/// button in half, and the preset gallery had its two named designs on the wrong bodies,
/// so a new install's look matched no preset and the picker opened blank.
/// </summary>
public class SettingsLayoutAndPresetTests
{
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
    /// Shows the form off-screen so the layout is real: an unshown form has no handles, and
    /// measuring it reports stale bounds (and Visible=false for everything).
    /// </summary>
    private static T WithShownDialog<T>(SettingsForm form, Func<SettingsForm, T> work)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        try
        {
            form.PerformLayout();
            Application.DoEvents();
            return work(form);
        }
        finally
        {
            form.Hide();
        }
    }

    /// <summary>Every ancestor's client area, so a control that overflows one is caught.</summary>
    private static void AssertInsideEveryParent(Control child)
    {
        Rectangle area = child.RectangleToScreen(child.ClientRectangle);

        for (Control? parent = child.Parent; parent is not null; parent = parent.Parent)
        {
            Rectangle box = parent.RectangleToScreen(parent.ClientRectangle);

            Assert.True(
                box.Contains(area),
                $"{child.Text} at {area} is not inside {parent.GetType().Name} at {box}");
        }
    }

    private static Button FindButton(Control root, string text)
    {
        if (root is Button button && string.Equals(button.Text, text, StringComparison.Ordinal))
        {
            return button;
        }

        foreach (Control child in root.Controls)
        {
            Button? found = FindButtonOrNull(child, text);
            if (found is not null)
            {
                return found;
            }
        }

        throw new Xunit.Sdk.XunitException($"the dialog has no button labelled \"{text}\"");
    }

    private static Button? FindButtonOrNull(Control root, string text)
    {
        if (root is Button button && string.Equals(button.Text, text, StringComparison.Ordinal))
        {
            return button;
        }

        foreach (Control child in root.Controls)
        {
            Button? found = FindButtonOrNull(child, text);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// The Reset nudges button is the lowest control in the Position group, so it is the
    /// first thing a short dialog cuts off. It has to sit inside the form, not merely exist
    /// in the control tree: a clipped button cannot be clicked.
    /// </summary>
    [Fact]
    public void TheResetNudgesButtonIsFullyInsideTheDialog()
    {
        RunOnUiThread(() =>
        {
            using var form = new SettingsForm(MonitorConfig.CreateInitial());

            return WithShownDialog(form, shown =>
            {
                Button reset = FindButton(shown, "Reset nudges");

                Assert.True(reset.Visible, "the Reset nudges button is hidden");
                AssertInsideEveryParent(reset);

                return true;
            });
        });
    }

    /// <summary>The footer has to stay reachable too, or the dialog cannot be accepted.</summary>
    [Fact]
    public void TheFooterButtonsAreInsideTheDialog()
    {
        RunOnUiThread(() =>
        {
            using var form = new SettingsForm(MonitorConfig.CreateInitial());

            return WithShownDialog(form, shown =>
            {
                foreach (string label in new[] { "OK", "Cancel" })
                {
                    AssertInsideEveryParent(FindButton(shown, label));
                }

                return true;
            });
        });
    }

    /// <summary>
    /// With saved looks, the saved-look picker adds height. The dialog still has to fit the
    /// 768 px netbook screen the existing height rule names, with everything inside it.
    /// </summary>
    [Fact]
    public void TheDialogWithASavedLookStillFitsAndKeepsItsButtons()
    {
        string folder = Path.Combine(Path.GetTempPath(), "tnm-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            string path = Path.Combine(folder, "presets.json");

            RunOnUiThread(() =>
            {
                var form = new SettingsForm(MonitorConfig.CreateInitial(), path);

                try
                {
                    return WithShownDialog(form, shown =>
                    {
                        shown.SavePresetForTest("A fairly long saved look name");
                        shown.PerformLayout();
                        Application.DoEvents();

                        Assert.True(shown.Height <= 740, $"dialog is {shown.Height} px tall");
                        Assert.True(shown.Width <= 1280, $"dialog is {shown.Width} px wide");

                        AssertInsideEveryParent(FindButton(shown, "Reset nudges"));
                        AssertInsideEveryParent(FindButton(shown, "OK"));

                        return true;
                    });
                }
                finally
                {
                    form.Dispose();
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The dialog opens on the preset a fresh install matches, so the user sees a name
    /// instead of an empty box. This is the case that was blank before the gallery was
    /// fixed: CreateInitial matched nothing.
    /// </summary>
    [Fact]
    public void ThePickerNamesThePresetAFreshInstallStartsFrom()
    {
        string? shown = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(MonitorConfig.CreateInitial());
            return form.SelectedPresetForTest;
        });

        Assert.Equal("HAS Preset", shown);
    }

    /// <summary>
    /// HAS Preset is the first-run design and Harbour is the plain green/red baseline, which
    /// is the pairing that was inverted. Named explicitly so a future reorder of the table
    /// cannot quietly swap them back.
    /// </summary>
    [Fact]
    public void TheTwoNamedDesignsAreOnTheBodiesTheirNamesDescribe()
    {
        WidgetPreset hasPreset = WidgetPresets.Find("HAS Preset")!;
        WidgetPreset harbour = WidgetPresets.Find("Harbour")!;

        // HAS Preset is whatever a fresh install actually writes.
        Assert.Equal(MonitorConfig.CreateInitial().Style, hasPreset.Style);
        Assert.True(WidgetPresets.AppearanceEquals(
            WidgetPresets.Apply(hasPreset, new MonitorConfig()),
            MonitorConfig.CreateInitial()));

        // Harbour is the classic constructor baseline the renderer and older tests use.
        Assert.Equal(new MonitorConfig().Style, harbour.Style);
        Assert.NotEqual(harbour.Style, hasPreset.Style);
        Assert.True(WidgetPresets.AppearanceEquals(
            WidgetPresets.Apply(harbour, new MonitorConfig()),
            new MonitorConfig().Normalize()));
    }

    /// <summary>The default entry is first, which is what "default in the dropdown" relies on.</summary>
    [Fact]
    public void TheFirstRunPresetIsTheFirstEntry()
    {
        Assert.Equal("HAS Preset", WidgetPresets.All[0].Name);
        Assert.Same(WidgetPresets.All[0], WidgetPresets.FindByAppearance(MonitorConfig.CreateInitial()));
    }

    /// <summary>
    /// The copy Setup seeds into %APPDATA% is what a new installation opens with, so it has
    /// to be the preset the dialog names - not merely a valid config.
    /// </summary>
    [Fact]
    public void TheInstallerSeedIsThePresetTheDialogNames()
    {
        string path = Path.Combine(SourceText.Root(), "installer", "config.default.json");
        MonitorConfig seeded = MonitorConfig.FromJson(File.ReadAllText(path));

        Assert.Same(WidgetPresets.All[0], WidgetPresets.FindByAppearance(seeded));
    }
}
