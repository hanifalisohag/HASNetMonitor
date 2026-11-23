using System.Drawing;
using System.Drawing.Imaging;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// A present that draws nothing must say so. The widget records a frame as presented
/// only when it was really drawn; if a failed present were recorded anyway, the widget
/// would believe it is up to date and never draw again - the "frozen, not updating"
/// failure, which the GPU and disk chips trigger because their appearance changes the
/// widget's size and so forces a new surface.
/// </summary>
public class PresentResultTests
{
    [SkippableFact]
    public void PresentReportsSuccessWhenItDraws()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);

            Assert.True(window.Present(bitmap, 30, 40));
        });
    }

    [SkippableFact]
    public void PresentReportsFailureWhenTheWindowIsGone()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);

            window.Dispose();

            // Nothing was drawn, so the caller must not record the frame as presented.
            Assert.False(window.Present(bitmap, 30, 40));
        });
    }

    [SkippableFact]
    public void PresentReportsFailureAfterTheWindowIsDisposed()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);

            Assert.True(window.Present(bitmap, 30, 40));
            window.Dispose();

            // A second present after disposal draws nothing and must report it.
            Assert.False(window.Present(bitmap, 30, 40));
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Native window test timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
