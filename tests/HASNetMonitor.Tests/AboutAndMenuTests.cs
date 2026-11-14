using System.Reflection;
using HASNetMonitor.Core;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

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
    public void EverythingTheUserCanSeeCarriesTheNewName()
    {
        // Product identity comes from the app project, spelled exactly as the About box.
        string project = SourceText.Read(".", "HASNetMonitor.csproj");
        Assert.Contains("<AssemblyName>HASNetMonitor</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains($"<Product>{AppInfo.Name}</Product>", project, StringComparison.Ordinal);
        Assert.Contains($"<AssemblyTitle>{AppInfo.Name}</AssemblyTitle>", project, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>HASNetMonitor</RootNamespace>", project, StringComparison.Ordinal);

        // The icon the project embeds is the file in Assets, under the product's name.
        Assert.Contains("Assets\\HASNetMonitor.ico", project, StringComparison.Ordinal);

        // The exe, the autostart value and the config folder are the names the OS sees.
        Assert.Equal("HASNetMonitor", StartupRegistration.ValueName);
        Assert.True(
            MonitorConfig.DefaultConfigPath.EndsWith(
                "HASNetMonitor" + Path.DirectorySeparatorChar + "config.json",
                StringComparison.OrdinalIgnoreCase),
            MonitorConfig.DefaultConfigPath);
    }

    /// <summary>
    /// The namespace rename, asserted without spelling the retired name: every source file
    /// must declare a namespace under the product's own root, so a reintroduced old one or a
    /// half-finished rename fails here.
    /// </summary>
    [Fact]
    public void EverySourceFileSitsUnderTheProductNamespace()
    {
        const string Root = "HASNetMonitor";
        Assert.Equal(Root, typeof(AppInfo).Namespace!.Split('.')[0]);
        Assert.Equal(Root + ".Core", typeof(AppInfo).Namespace);

        var problems = new List<string>();
        var polyfills = new HashSet<string>(StringComparer.Ordinal) { "IsExternalInit.cs", "NetFrameworkPolyfills.cs" };
        int declared = 0;
        foreach (string file in SourceText.AllSources())
        {
            string name = Path.GetFileName(file);
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                {
                    continue;
                }

                declared++;
                string ns = trimmed.Substring("namespace ".Length).Trim().TrimEnd(';', '{').Trim();

                // A polyfill has to sit in the framework namespace it stands in for.
                if (polyfills.Contains(name) && ns.StartsWith("System", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ns.StartsWith(Root, StringComparison.Ordinal))
                {
                    problems.Add($"{name} declares {ns}");
                }
            }
        }

        Assert.True(declared > 40, $"only {declared} namespaces found; the scan is not seeing the tree");
        Assert.Empty(problems);
    }

    [Fact]
    public void TheIdentityIsWhatTheUserIsTold()
    {
        Assert.Equal("HAS NetMonitor", AppInfo.Name);
        Assert.Equal("Md Hanif Ali Sohag", AppInfo.Author);
        Assert.Equal("hanifalisohag@gmail.com", AppInfo.ContactEmail);

        // A contact that cannot be written to is not a contact.
        int at = AppInfo.ContactEmail.IndexOf('@');
        Assert.True(at > 0, "the contact address has no local part");
        Assert.True(at < AppInfo.ContactEmail.Length - 1, "the contact address has no domain");
        Assert.False(AppInfo.ContactEmail.IndexOf(' ') >= 0, "the address contains a space");
        Assert.Contains('.', AppInfo.ContactEmail.Substring(at));
    }

    [Fact]
    public void TheVersionIsTheOneStampedOnTheAssembly()
    {
        string version = AppInfo.Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(AppInfo.ReadVersion(typeof(AppInfo).Assembly), version);

        // Set in the csproj as <Version>; a build metadata suffix must not leak into the UI.
        Assert.False(version.IndexOf('+') >= 0, "build metadata leaked into the version");
        Assert.False(version.IndexOf(' ') >= 0, "the version contains a space");

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
        Assert.Equal("HAS NetMonitor", product);

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
    public void TheTrayTooltipUsesTheSameNameAsTheAboutBox()
    {
        // The tooltip used to be its own hardcoded string and had already drifted from
        // the About box. It must come from AppInfo instead.
        string host = SourceText.Read("UI", "MonitorHost.cs");
        Assert.Contains("Text = AppInfo.Name", host, StringComparison.Ordinal);
        Assert.False(Mentions(host, "Text = \"HAS Net Monitor\""), "the tooltip is hardcoded again");
        Assert.False(Mentions(host, "Text = \"" + AppInfo.Name + "\""), "the tooltip is hardcoded again");
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
        Assert.False(Mentions(menu, "open settings folder"), "the tray still offers the settings folder");
        Assert.False(Mentions(menu, "OpenSettingsFolder"), "the folder-opening method is still there");
        Assert.False(Mentions(menu, "explorer.exe"), "the tray still shells out to Explorer");
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

    private static bool Mentions(string text, string needle)
        => text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

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
