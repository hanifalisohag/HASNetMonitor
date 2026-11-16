using System.Reflection;
using HASNetMonitor.Core;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The licence terms and where the app keeps its settings. Two rules the user asked for:
/// the default config is %APPDATA%\HASNetMonitor\config.json, and the licence requires
/// credit to the author, shown in the About box and the installer as well as in LICENSE.
/// </summary>
public class LicenseAndConfigPathTests
{
    [Fact]
    public void TheDefaultConfigSitsInTheRoamingAppDataFolder()
    {
        string path = MonitorConfig.DefaultConfigPath;

        // The exact shape the user asked for, built from the Windows special folder so it
        // follows the real profile rather than a hardcoded "C:\Users\...".
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HASNetMonitor",
                "config.json"),
            path);

        // ...which on this machine is %APPDATA%\HASNetMonitor\config.json.
        Assert.True(
            path.EndsWith(
                Path.Combine("AppData", "Roaming", "HASNetMonitor", "config.json"),
                StringComparison.OrdinalIgnoreCase),
            path);

        // Roaming, not Local: the folder name the user named is the roaming one.
        Assert.DoesNotContain(
            Path.Combine("AppData", "Local", "HASNetMonitor"),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheConfigIsWrittenWhereTheDefaultSays()
    {
        // The default path is only real if saving to it works, which is also how a
        // first-run user gets a config.json without creating anything by hand.
        string folder = Path.Combine(Path.GetTempPath(), "tnm-cfgpath-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "HASNetMonitor", "config.json");
        try
        {
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)!));

            MonitorConfig config = TestSupport.BarsOnly();
            Assert.True(config.TrySave(path));
            Assert.True(File.Exists(path));
            Assert.Equal(config.ToJson(), MonitorConfig.Load(path).ToJson());
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

    [Fact]
    public void TheRepositoryCarriesTheLicenseFile()
    {
        string text = SourceText.Read(".", "LICENSE");

        Assert.True(LicenseInfo.IsTheProjectLicense(text), "LICENSE is not the project's licence");

        // The attribution requirement is stated, not just implied by the MIT text.
        Assert.Contains("Md Hanif Ali Sohag", text, StringComparison.Ordinal);
        Assert.Contains("Copyright", text, StringComparison.Ordinal);
        Assert.Contains("credit", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLicenseShipsInsideTheAssembly()
    {
        // The About box reads this resource, so a missing embed means an empty dialog.
        string text = LicenseInfo.Text;

        Assert.False(string.IsNullOrWhiteSpace(text), "the LICENSE resource is not embedded");
        Assert.True(LicenseInfo.IsTheProjectLicense(text), "the embedded text is not the project's licence");

        // It is the file on disk, not a second copy that could drift.
        Assert.Equal(
            SourceText.Read(".", "LICENSE").Replace("\r\n", "\n").TrimEnd(),
            text);
    }

    [Fact]
    public void AMissingResourceReadsAsEmptyInsteadOfThrowing()
    {
        // The About box opens even when the text is absent; this is that path.
        Assert.Equal(string.Empty, LicenseInfo.Read(typeof(string).Assembly));
        Assert.Throws<ArgumentNullException>(() => LicenseInfo.Read(null!));
    }

    [Fact]
    public void TheIdentityNamesTheLicenseAndItsAttribution()
    {
        Assert.Equal("MIT License", AppInfo.LicenseName);
        Assert.Contains(AppInfo.Author, AppInfo.LicenseCredit, StringComparison.Ordinal);
        Assert.Contains(AppInfo.LicenseName, AppInfo.LicenseSummary, StringComparison.Ordinal);
        Assert.Contains(AppInfo.LicenseCredit, AppInfo.LicenseSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAboutBoxShowsTheLicenseAndTheCredit()
    {
        (string text, Size size) = RunOnUiThread(() =>
        {
            using var form = new AboutForm();
            form.CreateControl();
            return (form.AllText(), form.PreferredSize);
        });

        // The credit is on the dialog itself, not only behind the link.
        Assert.Contains("License:", text, StringComparison.Ordinal);
        Assert.Contains(AppInfo.LicenseName, text, StringComparison.Ordinal);
        Assert.Contains(AppInfo.Author, text, StringComparison.Ordinal);

        // ...and the full text is one click away.
        Assert.Contains("View full license", text, StringComparison.Ordinal);

        // Two extra rows grow the box, but it must still be a dialog, not a strip.
        Assert.InRange(size.Width, 240, 640);
        Assert.InRange(size.Height, 150, 560);
    }

    [Fact]
    public void TheInstallerShowsTheSameLicense()
    {
        string iss = SourceText.Read("installer", "HASNetMonitor.iss");

        // The licence page is on, and it points at the same file the assembly embeds.
        Assert.Contains("LicenseFile=..\\LICENSE", iss, StringComparison.Ordinal);

        // The publisher named in Apps & features is the author the credit must name.
        Assert.Contains("AppPublisher=" + AppInfo.Author, iss, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectStampsTheLicenseAndTheAuthor()
    {
        string project = SourceText.Read(".", "HASNetMonitor.csproj");

        // The exe's file properties carry the credit, and the LICENSE file is embedded.
        Assert.Contains("<Copyright>Copyright (c) 2026 Md Hanif Ali Sohag</Copyright>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project, StringComparison.Ordinal);
        Assert.Contains(
            "<EmbeddedResource Include=\"LICENSE\" LogicalName=\"HASNetMonitor.LICENSE\" />",
            project,
            StringComparison.Ordinal);

        // The embedded resource name the loader asks for is the one the project gives it.
        Assert.Contains($"LogicalName=\"{LicenseInfo.ResourceName}\"", project, StringComparison.Ordinal);
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
