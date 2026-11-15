using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The dialog applies every change live: the preview picture and the running widget
/// both follow the controls as they are touched, before OK is pressed. A control that
/// is not wired into that path looks broken - the user picks a value, nothing moves,
/// and only OK makes it appear.
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
    public void ThePreviewPictureFollowsTheArrowTypeBeforeOk()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            form.CreateControl();

            SetCombo(form, "_arrowStyle", ArrowStyles.Classic);
            string classic = PreviewSignature(form);

            SetCombo(form, "_arrowStyle", ArrowStyles.None);
            string none = PreviewSignature(form);

            // The two pictures must differ: the preview is redrawn from the controls, so a
            // style that never reaches it would leave the same bitmap in place.
            Assert.NotEqual(classic, none);
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

            foreach (string field in new[] { "_arrowSize", "_fontSize", "_barThickness", "_segmentGap" })
            {
                var number = (NumericUpDown)Field(form, field);
                int before = seen.Count;
                number.Value = number.Value == number.Minimum ? number.Maximum : number.Minimum;
                Assert.True(seen.Count > before, $"{field} is not wired for live updates");
            }
        });
    }

    /// <summary>
    /// A cheap fingerprint of the preview picture: its size and a sample of its pixels.
    /// Taken immediately, because the dialog disposes the previous bitmap when it
    /// replaces it.
    /// </summary>
    private static string PreviewSignature(SettingsForm form)
    {
        var image = (Image?)Field(form, "_preview", "Image");
        Assert.NotNull(image);

        using var bitmap = new Bitmap(image!);
        var parts = new List<string> { $"{bitmap.Width}x{bitmap.Height}" };
        for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 8))
        {
            for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 8))
            {
                parts.Add(bitmap.GetPixel(x, y).ToArgb().ToString("X8"));
            }
        }

        return string.Join(",", parts);
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
