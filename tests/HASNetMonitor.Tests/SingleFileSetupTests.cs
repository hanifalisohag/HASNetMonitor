using HASNetMonitor.Core;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Tests;

/// <summary>
/// The app ships as a single exe: no HASNetMonitor.exe.config beside it. .NET Framework
/// took its DPI awareness from that file, so the code has to claim the same mode itself,
/// before it creates a window - and the build must not put the file back.
/// </summary>
public class SingleFileSetupTests
{
    [Fact]
    public void NoAppConfigExistsToShip()
    {
        string root = SourceText.Root();

        Assert.False(
            File.Exists(Path.Combine(root, "App.config")),
            "App.config is back; it would produce a HASNetMonitor.exe.config to ship");

        // No configuration file of any kind at the project root: the whole point is that
        // nothing has to be copied beside the exe.
        Assert.Empty(Directory.GetFiles(root, "*.config", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void TheDpiModeLivesInCodeInsteadOfAConfigFile()
    {
        string source = SourceText.Read("Core", "DpiAwareness.cs");

        Assert.Contains("SetProcessDpiAwarenessContext", SourceText.Read("Interop", "NativeMethods.cs"), StringComparison.Ordinal);
        Assert.Contains("PerMonitorV2", source, StringComparison.Ordinal);
        Assert.Contains("SetProcessDPIAware", SourceText.Read("Interop", "NativeMethods.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildDoesNotGenerateAnExeConfig()
    {
        string project = SourceText.Read(".", "HASNetMonitor.csproj");

        Assert.Contains("<GenerateSupportedRuntime>false</GenerateSupportedRuntime>", project, StringComparison.Ordinal);
        Assert.Contains("<SkipCopyAppConfigToOutput>true</SkipCopyAppConfigToOutput>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoAppConfigAssumptionIsOnlyMadeForNetFramework()
    {
        // The net11 target reads ApplicationHighDpiMode from the project file, so the
        // hand-made call must not run there: it would fight the runtime and fail.
        string source = SourceText.Read(".", "Program.cs");
        int apply = source.IndexOf("DpiAwareness.Apply()", StringComparison.Ordinal);
        Assert.True(apply >= 0, "Program.cs never sets DPI awareness");

        int frameworkStart = source.LastIndexOf("#if NETFRAMEWORK", apply, StringComparison.Ordinal);
        Assert.True(frameworkStart >= 0 && frameworkStart < apply, "the DPI call is outside the net48 branch");
    }

    [Fact]
    public void DpiAwarenessIsSetBeforeAnyWindowIsCreated()
    {
        string source = SourceText.Read(".", "Program.cs");

        int apply = source.IndexOf("DpiAwareness.Apply()", StringComparison.Ordinal);
        int run = source.IndexOf("Application.Run", StringComparison.Ordinal);

        Assert.True(apply >= 0, "Program.cs never sets DPI awareness");
        Assert.True(run > apply, $"DPI awareness is set at {apply}, after Application.Run at {run}");
    }

    [Fact]
    public void TheProcessActuallyBecomesDpiAware()
    {
        // Runs the real calls: on Windows 10 1703+ this makes the process per-monitor v2,
        // which is exactly what the removed App.config used to declare.
        DpiAwarenessResult result = DpiAwareness.Apply();

        Assert.True(result.IsAware(), $"apply returned {result}");
        Assert.True(NativeMethods.TryGetProcessDpiAwareness(out int awareness), "the API should exist on this Windows");
        Assert.True(awareness > 0, $"the process is still DPI-unaware (awareness {awareness})");
    }

    [Fact]
    public void SettingItTwiceIsHarmless()
    {
        // Main runs once, but a second call must not throw or downgrade the process.
        DpiAwareness.Apply();
        DpiAwarenessResult second = DpiAwareness.Apply();

        Assert.True(second.IsAware(), $"a second call returned {second}");
        Assert.True(NativeMethods.TryGetProcessDpiAwareness(out int awareness));
        Assert.True(awareness > 0);
    }

    [Fact]
    public void EveryHResultIsTreatedAsAwareOrNot()
    {
        // Only PerMonitorV2 and SystemAware count as aware; Unavailable must not.
        Assert.True(DpiAwarenessResult.PerMonitorV2.IsAware());
        Assert.True(DpiAwarenessResult.SystemAware.IsAware());
        Assert.False(DpiAwarenessResult.Unavailable.IsAware());
    }
}
