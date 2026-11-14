using System.Reflection;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// The identity shown to the user (name, version, author, contact) and the tray menu:
/// the About entry exists, the settings-folder entry is gone, and saving a setting
/// needs nothing from the user beyond picking it.
/// </summary>
public class AboutAndMenuTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tnm-about-" + Guid.NewGuid().ToString("N"));

    public AboutAndMenuTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheIdentityIsWhatTheUserIsTold()
    {
        Assert.Equal("Taskbar Net Monitor", AppInfo.Name);
        Assert.Equal("Md Hanif Ali Sohag", AppInfo.Author);
        Assert.Equal("hanifalisohag@gmail.com", AppInfo.ContactEmail);

        // A contact that cannot be written to is not a contact.
        int at = AppInfo.ContactEmail.IndexOf('@');
        Assert.True(at > 0, "the contact address has no local part");
        Assert.True(at < AppInfo.ContactEmail.Length - 1, "the contact address has no domain");
        Assert.DoesNotContain(' ', AppInfo.ContactEmail);
        Assert.Contains('.', AppInfo.ContactEmail.Substring(at));
    }

    [Fact]
    public void TheVersionIsTheOneStampedOnTheAssembly()
    {
        string version = AppInfo.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(AppInfo.ReadVersion(typeof(AppInfo).Assembly), version);

        // Set in the csproj as <Version>; a build metadata suffix must not leak into the UI.
        Assert.DoesNotContain('+', version);
        Assert.DoesNotContain(' ', version);

        Version parsed = Version.Parse(version);
        Assert.True(parsed.Major >= 1, $"version {version} looks unset");
        Assert.Equal(version, AppInfo.Title.Substring(AppInfo.Title.LastIndexOf(' ') + 1));
    }

    [Fact]
    public void TheStampedVersionReachesTheExeFileProperties()
    {
        Assembly assembly = typeof(AppInfo).Assembly;

        // These are what Explorer's Properties > Details shows, so the About box and the
        // file on disk cannot disagree.
        Assert.NotNull(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>());
        Assert.NotNull(assembly.GetCustomAttribute<AssemblyProductAttribute>());

        string product = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Assert.Equal("Taskbar Net Monitor", product);

        string authors = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;
        Assert.Equal(AppInfo.Author, authors);
    }

    [Theory]
    [InlineData("1.2.3+abcdef0", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("  2.0.0+deadbee  ", "2.0.0")]
    [InlineData("+abcdef0", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void BuildMetadataIsStrippedFromTheVersion(string? raw, string? expected)
    {
        Assert.Equal(expected, AppInfo.TrimBuildMetadata(raw));
    }

    [Fact]
    public void TheAboutBoxShowsNameVersionAuthorAndContact()
    {
        (string text, Size size, bool hasIcon) = RunOnUiThread(() =>
        {
            using var form = new AboutForm();
            form.CreateControl();
            return (form.AllText(), form.PreferredSize, form.Icon is not null);
        });

        Assert.Contains(AppInfo.Name, text, StringComparison.Ordinal);
        Assert.Contains($"Version {AppInfo.Version}", text, StringComparison.Ordinal);
        Assert.Contains(AppInfo.Author, text, StringComparison.Ordinal);
        Assert.Contains(AppInfo.ContactEmail, text, StringComparison.Ordinal);

        // Each fact is on its own line, so the dialog reads as labelled facts.
        Assert.Contains($"Author: {AppInfo.Author}", text, StringComparison.Ordinal);
        Assert.Contains($"Contact: {AppInfo.ContactEmail}", text, StringComparison.Ordinal);

        // A dialog that collapses or grows to the screen edge is a layout bug: these
        // bounds are what the measured layout produces with room to spare.
        Assert.InRange(size.Width, 240, 640);
        Assert.InRange(size.Height, 150, 520);

        // The box shows the same mark as the exe and the tray, never the stock one.
        Assert.True(hasIcon, "the About box has no application icon");
    }

    [Fact]
    public void TheTrayMenuOffersAboutAndNoFileManagement()
    {
        string menu = SourceText.Read("UI", "MonitorHost.cs");

        // About is reachable from the tray, and it opens the dialog above.
        Assert.Contains("About", menu, StringComparison.Ordinal);
        Assert.Contains("new AboutForm()", menu, StringComparison.Ordinal);

        // Nothing in the menu makes the user handle files, so no folder opening, no
        // Explorer shell-out and no path display is left.
        Assert.DoesNotContain("Open settings folder", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenSettingsFolder", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("explorer.exe", menu, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryTraySwitchSavesItselfWhenClicked()
    {
        // The tray switches are the paths a user actually uses; each must persist the
        // change it just made, otherwise the setting is lost on restart and the user
        // would have to hunt for the file.
        string applyLook = SourceText.ExtractMethod("UI", "MonitorHost.cs", "private void ApplyLook(");
        Assert.Contains("TrySave", applyLook, StringComparison.Ordinal);

        string applyStartup = SourceText.ExtractMethod("UI", "MonitorHost.cs", "private void ApplyStartup(");
        Assert.Contains("TrySave", applyStartup, StringComparison.Ordinal);

        // Accepting the settings dialog persists the result too.
        string showSettings = SourceText.ExtractMethod("UI", "MonitorHost.cs", "private void ShowSettings(");
        Assert.Contains("TrySave", showSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedSettingIsWrittenWithoutTheUserTouchingAFile()
    {
        // The config folder does not exist yet: saving has to create it, so a first-run
        // user never has to create anything by hand.
        string path = Path.Combine(_folder, "nested", "config.json");
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)!));

        MonitorConfig config = TestSupport.BarsOnly();
        config.OffsetFromTrayPx = 19;
        config.Style = WidgetStyles.Dots;

        Assert.True(config.TrySave(path));

        Assert.True(File.Exists(path));
        MonitorConfig reloaded = MonitorConfig.Load(path);
        Assert.Equal(19, reloaded.OffsetFromTrayPx);
        Assert.Equal(WidgetStyles.Dots, reloaded.Style);

        // Saving again from the reloaded state is stable, which is what makes an
        // automatic save on every change safe.
        Assert.True(reloaded.TrySave(path));
        Assert.Equal(reloaded.ToJson(), MonitorConfig.Load(path).ToJson());
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
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("the dialog threw", failure);
        }

        return result;
    }
}
