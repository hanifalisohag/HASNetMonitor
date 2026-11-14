using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The settings dialog must actually offer the new look options: a control missing from
/// the form would silently reset that choice every time the user pressed OK.
/// </summary>
public sealed class SettingsDialogTests
{
    [Fact]
    public void TheDialogKeepsEveryLookOptionWhenItIsAccepted()
    {
        MonitorConfig original = TestSupport.BarsOnly().Clone();
        original.Style = WidgetStyles.Sidebar;
        original.SortOrder = StackOrder.UploadFirst;
        original.FontFamily = FontCatalog.InstalledFamilies[0];
        original.FontBold = true;
        original.FontItalic = true;
        original.RoundedBars = true;
        original.AccentGlow = true;
        original.ShowText = true;
        original.Normalize();

        MonitorConfig result = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(original.Clone());

            // The dialog opens without a picture error and shows the new choices.
            Assert.True(form.PreferredSize.Width > 0 && form.PreferredSize.Height > 0);

            return form.BuildResult();
        });

        Assert.Equal(WidgetStyles.Sidebar, result.Style);
        Assert.Equal(StackOrder.UploadFirst, result.SortOrder);
        Assert.Equal(original.FontFamily, result.FontFamily);
        Assert.True(result.FontBold);
        Assert.True(result.FontItalic);
        Assert.True(result.RoundedBars);
        Assert.True(result.AccentGlow);
    }

    [Fact]
    public void TheDialogFitsOnAScreen()
    {
        Size preferred = RunOnUiThread(() =>
        {
            using var form = new SettingsForm(new MonitorConfig());
            return form.PreferredSize;
        });

        Assert.InRange(preferred.Width, 320, 1920);
        Assert.InRange(preferred.Height, 240, 1080);
    }

    [Fact]
    public void EveryStyleCanBeOpenedInTheDialog()
    {
        foreach (string style in WidgetStyles.All)
        {
            foreach (string order in StackOrder.All)
            {
                MonitorConfig config = TestSupport.BarsOnly().Clone();
                config.Style = style;
                config.SortOrder = order;
                config.FontBold = true;
                config.FontItalic = true;
                config.AccentGlow = true;
                config.RoundedBars = true;
                config.Normalize();

                MonitorConfig result = RunOnUiThread(() =>
                {
                    using var form = new SettingsForm(config.Clone());
                    return form.BuildResult();
                });

                Assert.Equal(style, result.Style);
                Assert.Equal(order, result.SortOrder);
            }
        }
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
