using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// Guards the bug that made the widget vanish for good: the render path used to
/// bail out whenever the shell owned the foreground window. On a normal desktop the
/// foreground is the desktop (Progman) almost all of the time, so the widget stopped
/// drawing and stopped repairing itself and never came back while the app kept
/// running. Rendering and repairing must never depend on who owns the foreground.
/// </summary>
public sealed class RenderIsNeverGatedOnFocusTests
{
    /// <summary>
    /// The removed foreground heuristic, spelled indirectly so that this guard does
    /// not itself reintroduce the identifier into the code base.
    /// </summary>
    private static readonly string RemovedHeuristic = string.Concat("ShellUI", "Active");



    [Fact]
    public void TheRenderPathDoesNotConsultTheForegroundWindow()
    {
        string render = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void Render()");

        Assert.DoesNotContain("GetForegroundWindow", render, StringComparison.Ordinal);
        Assert.DoesNotContain(RemovedHeuristic, render, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRepairPathDoesNotConsultTheForegroundWindowEither()
    {
        string repair = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void RecoverIfCovered(");

        Assert.DoesNotContain("GetForegroundWindow", repair, StringComparison.Ordinal);
        Assert.DoesNotContain(RemovedHeuristic, repair, StringComparison.Ordinal);
    }

    [Fact]
    public void TheForegroundHeuristicIsGoneForGood()
    {
        // That heuristic treated "the desktop is focused" as "the shell is busy",
        // which disabled drawing and repairs in the widget's normal state.
        Assert.DoesNotContain(
            RemovedHeuristic,
            SourceText.Read("UI", "TaskbarWidget.cs"),
            StringComparison.Ordinal);
    }

}
