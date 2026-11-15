using System.Reflection;
using System.Windows.Forms;
using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The dialog applies every change live: the running widget follows the controls as
/// they are touched, before OK is pressed. A control that is not wired into that path
/// looks broken - the user picks a value, nothing moves, and only OK makes it appear.
/// The dialog has no preview of its own, so the live widget is the only feedback.
/// </summary>
public sealed class LivePreviewTests
{
    [Theory]
    [InlineData(ArrowStyles.Solid)]
    [InlineData(ArrowStyles.Small)]
    [InlineData(ArrowStyles.Double)]
    [InlineData(ArrowStyles.Chevron)]
    [InlineData(ArrowStyles.Bar)]
    [InlineData(ArrowStyles.None)]
    [InlineData(ArrowStyles.Classic)]
    public void ChoosingAnArrowTypeReachesTheLiveWidgetBeforeOk(string style)
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            int before = seen.Count;

            // Move away first: selecting the value that is already selected raises no
            // change event, and that is not what this test is about.
            SetCombo(form, "_arrowStyle", style == ArrowStyles.Classic ? ArrowStyles.Solid : ArrowStyles.Classic);
            before = seen.Count;
            SetCombo(form, "_arrowStyle", style);

            Assert.True(seen.Count > before, $"choosing {style} must reach the live widget");
            Assert.Equal(style, seen[^1].ArrowStyle);
        });
    }

    [Theory]
    [InlineData(TextColorModes.Bar)]
    [InlineData(TextColorModes.Custom)]
    [InlineData(TextColorModes.Auto)]
    public void ChoosingATextColourModeReachesTheLiveWidgetBeforeOk(string mode)
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            int before = seen.Count;
            SetCombo(form, "_downloadTextMode", mode == TextColorModes.Auto ? TextColorModes.Bar : TextColorModes.Auto);
            before = seen.Count;
            SetCombo(form, "_downloadTextMode", mode);

            Assert.True(seen.Count > before, $"choosing {mode} must reach the live widget");
            Assert.Equal(mode, seen[^1].DownloadTextColorMode);
        });
    }

    [Fact]
    public void EveryPickerInTheDialogIsWiredForLiveUpdates()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            // Every combo and spinner the dialog owns must report a change. A control that
            // is not wired is a control the user has to press OK to see.
            foreach (string field in new[]
            {
                "_style", "_sortOrder", "_fontFamily", "_arrowStyle",
                "_downloadTextMode", "_uploadTextMode",
            })
            {
                var combo = (ComboBox)Field(form, field);
                Assert.True(combo.Items.Count > 1, $"{field} has nothing to choose");

                int before = seen.Count;
                combo.SelectedIndex = combo.SelectedIndex == 0 ? 1 : 0;
                Assert.True(seen.Count > before, $"{field} is not wired for live updates");
            }

            foreach (string field in new[]
            {
                "_arrowSize", "_fontSize", "_barThickness", "_segmentGap",
                "_nudgeX", "_nudgeY", "_offsetFromTray", "_maxScale", "_interval", "_backdropOpacity",
            })
            {
                var number = (NumericUpDown)Field(form, field);
                int before = seen.Count;
                number.Value = number.Value == number.Minimum ? number.Maximum : number.Minimum;
                Assert.True(seen.Count > before, $"{field} is not wired for live updates");
            }
        });
    }

    [Fact]
    public void EveryCheckBoxInTheDialogIsWiredForLiveUpdates()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            foreach (string field in new[]
            {
                "_bold", "_italic", "_roundedBars", "_accentGlow", "_showText", "_showBackdrop",
                "_crispText", "_sharedScale", "_autoScale", "_startWithWindows",
            })
            {
                var check = (CheckBox)Field(form, field);
                int before = seen.Count;
                check.Checked = !check.Checked;
                Assert.True(seen.Count > before, $"{field} is not wired for live updates");
            }
        });
    }

    /// <summary>
    /// The dialog must not carry a preview of its own: the live widget is the feedback,
    /// so a second picture would only show a stale or duplicated rendering.
    /// </summary>
    [Fact]
    public void TheDialogHasNoPreviewOfItsOwn()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            form.CreateControl();

            // The preview is gone, so nothing in the dialog may render a second copy of
            // the widget: no picture box, and no group that offers one.
            Assert.DoesNotContain(AllControls(form), c => c is PictureBox);
            Assert.DoesNotContain(AllControls(form), c => c is GroupBox g && g.Text == "Preview");
        });
    }

    /// <summary>
    /// The live path must carry the value the user just picked, not the value the dialog
    /// opened with: a control wired to the wrong field would still raise an event.
    /// </summary>
    [Fact]
    public void TheLiveConfigCarriesTheValueJustPicked()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            var thickness = (NumericUpDown)Field(form, "_barThickness");
            thickness.Value = thickness.Value == thickness.Minimum ? thickness.Maximum : thickness.Minimum;
            Assert.Equal((int)thickness.Value, seen[^1].BarThicknessPx);

            var autoScale = (CheckBox)Field(form, "_autoScale");
            autoScale.Checked = !autoScale.Checked;
            Assert.Equal(autoScale.Checked, seen[^1].AutoScale);

            var interval = (NumericUpDown)Field(form, "_interval");
            interval.Value = interval.Value == interval.Minimum ? interval.Maximum : interval.Minimum;
            Assert.Equal((int)interval.Value, seen[^1].RefreshIntervalMs);
        });
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in AllControls(child))
            {
                yield return nested;
            }
        }
    }

    private static void SetCombo(SettingsForm form, string field, string key)
    {
        var combo = (ComboBox)Field(form, field);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            object? item = combo.Items[i];
            string? itemKey = item?.GetType().GetProperty("Key")?.GetValue(item) as string;
            if (string.Equals(itemKey, key, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        Assert.Fail($"{field} does not offer {key}");
    }

    private static object Field(SettingsForm form, string name, string? property = null)
    {
        FieldInfo info = typeof(SettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"no field {name}");
        object value = info.GetValue(form)!;
        return property is null
            ? value
            : value.GetType().GetProperty(property)!.GetValue(value)!;
    }

    private static void RunOnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }
}
