using System.Xml.Linq;

namespace HASNetMonitor.Tests;

/// <summary>
/// The three shipping artefacts the request asked for: the Inno Setup script that bundles
/// the net48 publish output, the GitHub Actions workflow that builds it on every push to
/// main, and the uninstall prompt that deletes config.json only when its box is ticked.
/// None of these are reachable from a test run, so the rules are asserted against the
/// sources themselves.
/// </summary>
public class InstallerAndWorkflowTests
{
    private const string PublishCommand = "dotnet publish HASNetMonitor.csproj -c Release -f net48 -o publish";

    private static readonly Lazy<string> IssText =
        new(() => SourceText.Read("installer", "HASNetMonitor.iss"));

    private static string Iss => IssText.Value;

    /// <summary>The [Code] section, where the uninstall prompt lives.</summary>
    private static string CodeSection()
    {
        int start = Iss.IndexOf("[Code]", StringComparison.Ordinal);
        Assert.True(start >= 0, "the .iss has no [Code] section");
        return Iss[start..];
    }

    private static string ProjectVersion()
    {
        string path = Path.Combine(SourceText.Root(), "HASNetMonitor.csproj");
        XDocument project = XDocument.Load(path);
        string? version = project.Descendants("Version").FirstOrDefault()?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(version), "HASNetMonitor.csproj has no <Version>");
        return version!;
    }

    /// <summary>
    /// The workflow that builds this installer. Found by content, so renaming the file does
    /// not break the test - but there must be exactly one, or two builds could publish
    /// artefacts under the same name.
    /// </summary>
    private static string Workflow(out string fileName)
    {
        string folder = Path.Combine(SourceText.Root(), ".github", "workflows");
        Assert.True(Directory.Exists(folder), $"{folder} does not exist");

        var matches = new List<string>();
        foreach (string file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            if (text.Contains("HASNetMonitor.iss", StringComparison.Ordinal))
            {
                matches.Add(file);
            }
        }

        Assert.True(
            matches.Count == 1,
            $"expected one workflow compiling HASNetMonitor.iss, found {matches.Count}: "
                + string.Join(", ", matches.Select(Path.GetFileName)));

        fileName = Path.GetFileName(matches[0]);
        return File.ReadAllText(matches[0]);
    }

    /// <summary>
    /// The invariants that tie the .iss to the app's own code. A value copied into the
    /// script that drifts from the constant the exe uses breaks install or uninstall in a
    /// way no other test would notice.
    /// </summary>
    private static void AssertScriptMatchesApp()
    {
        // Single-instance check: Setup/Uninstall refuse to run over a live widget.
        string program = SourceText.Read(".", "Program.cs");
        Assert.Contains(@"Local\HASNetMonitor.Instance", program, StringComparison.Ordinal);
        Assert.Contains("AppMutex=Local\\HASNetMonitor.Instance", Iss, StringComparison.Ordinal);

        // The Run value the app writes itself and Uninstall must clear.
        string startup = SourceText.Read("Core", "StartupRegistration.cs");
        Assert.Contains(@"public const string ValueName = ""HASNetMonitor""", startup, StringComparison.Ordinal);
        Assert.Contains("#define RunValueName \"HASNetMonitor\"", Iss, StringComparison.Ordinal);

        // The settings folder asked about at uninstall.
        string config = SourceText.Read("Core", "MonitorConfig.cs");
        Assert.Contains("\"HASNetMonitor\"", config, StringComparison.Ordinal);
        Assert.Contains("#define ConfigFolder \"HASNetMonitor\"", Iss, StringComparison.Ordinal);

        // A manual build passes no /DAppVersion, so the fallback must not mislabel the exe.
        Assert.Contains($"#define AppVersion \"{ProjectVersion()}\"", Iss, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstallerBundlesThePublishFolder()
    {
        // The publish output is the only input: one exe, nothing to install beside it.
        Assert.Contains("#define PublishDir \"..\\publish\"", Iss, StringComparison.Ordinal);
        Assert.Contains("Source: \"{#PublishDir}\\{#AppExeName}\"", Iss, StringComparison.Ordinal);
        Assert.Contains("#define AppExeName \"HASNetMonitor.exe\"", Iss, StringComparison.Ordinal);

        // The .iss documents the same command the workflow runs, byte for byte.
        Assert.Contains(PublishCommand, Iss, StringComparison.Ordinal);

        string root = SourceText.Root();
        Assert.True(
            File.Exists(Path.Combine(root, "Assets", "HASNetMonitor.ico")),
            "SetupIconFile points at Assets\\HASNetMonitor.ico, which does not exist");
        Assert.Contains("SetupIconFile=..\\Assets\\HASNetMonitor.ico", Iss, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheInstallerIsPerUserAndNeedsNoAdminRights()
    {
        // README promises "no admin rights needed"; PrivilegesRequired is what enforces it.
        Assert.Contains("PrivilegesRequired=lowest", Iss, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={autopf}\\{#AppFolderName}", Iss, StringComparison.Ordinal);

        // A stable AppId is what makes an upgrade replace the old install.
        Assert.Contains("AppId={{7D4E2C1A-58B3-4F6E-9C2D-1E7A6B0F3D94}", Iss, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheWorkflowBuildsOnEveryPushToMain()
    {
        string workflow = Workflow(out string fileName);

        Assert.Contains("push:", workflow, StringComparison.Ordinal);
        Assert.Contains("branches: [main]", workflow, StringComparison.Ordinal);
        Assert.True(
            workflow.Contains("runs-on: windows", StringComparison.Ordinal),
            $"{fileName} does not use a Windows runner, so Inno Setup cannot run");

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheWorkflowPublishesWhatTheInstallerBundles()
    {
        string workflow = Workflow(out _);

        Assert.Contains(PublishCommand, workflow, StringComparison.Ordinal);

        // The compile step has to use the script the tests just checked, with an explicit
        // compiler path (Inno Setup 6 is preinstalled on the images but not on PATH) and a
        // failing exit code must fail the job.
        Assert.Contains("ISCC", workflow, StringComparison.Ordinal);
        Assert.Contains("/DAppVersion=$env:APP_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("$LASTEXITCODE", workflow, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheWorkflowTakesTheVersionFromTheProjectFile()
    {
        string workflow = Workflow(out _);

        // One source of version truth: <Version> in the project, which AppInfo reads back.
        Assert.Contains("HASNetMonitor.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("APP_VERSION=", workflow, StringComparison.Ordinal);

        // A hard-coded version here would silently disagree with the exe it wraps.
        Assert.DoesNotContain(
            $"APP_VERSION={ProjectVersion()}",
            workflow,
            StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheWorkflowUploadsTheSetupExeItNamedInTheScript()
    {
        string workflow = Workflow(out _);
        string baseName = "OutputBaseFilename=HASNetMonitor-Setup-{#AppVersion}";

        Assert.Contains(baseName, Iss, StringComparison.Ordinal);

        // The artefact glob must match OutputBaseFilename, or the job uploads nothing.
        Assert.Contains("installer/Output/HASNetMonitor-Setup-*.exe", workflow, StringComparison.Ordinal);

        // And a missing exe must fail the job rather than upload an empty artefact.
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheWorkflowAsksForTheSdkTheProjectTargets()
    {
        string workflow = Workflow(out _);

        Assert.Contains("actions/setup-dotnet", workflow, StringComparison.Ordinal);
        Assert.Contains("11.0.x", workflow, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheUninstallPromptIsACheckBoxRatherThanAQuestion()
    {
        string code = CodeSection();

        Assert.Contains("TNewCheckBox.Create", code, StringComparison.Ordinal);
        Assert.Contains("DeleteCheckBox.Checked := False", code, StringComparison.Ordinal);

        // The old yes/no box is gone: one dialog, and the answer is the box's state.
        Assert.DoesNotContain("MsgBox(", code[code.IndexOf("CurUninstallStepChanged", StringComparison.Ordinal)..], StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheSettingsAreDeletedOnlyWhenTheBoxIsTicked()
    {
        string code = CodeSection();
        const string guard = "if DeleteCheckBox.Checked then";

        int guardAt = code.IndexOf(guard, StringComparison.Ordinal);
        int deleteAt = code.IndexOf("DelTree(", StringComparison.Ordinal);

        Assert.True(guardAt >= 0, "nothing checks the box before deleting the settings");
        Assert.True(deleteAt > guardAt, "DelTree runs before the box is consulted");

        // Exactly one deletion, and the guard is the whole condition on it: nothing else
        // (a stray "begin", another test) can run it when the box is unticked.
        Assert.Equal(1, CountOf(code, "DelTree("));
        Assert.True(
            string.IsNullOrWhiteSpace(code[(guardAt + guard.Length)..deleteAt]),
            $"the deletion is not directly guarded by the box: '{code[(guardAt + guard.Length)..deleteAt]}'");

        AssertScriptMatchesApp();
    }

    [Fact]
    public void BackingOutOfThePromptKeepsTheSettings()
    {
        string code = CodeSection();
        int show = code.IndexOf("PromptForm.ShowModal", StringComparison.Ordinal);
        int guardAt = code.IndexOf("if DeleteCheckBox.Checked then", StringComparison.Ordinal);

        Assert.True(show >= 0, "the prompt is never shown");
        Assert.True(guardAt > show, "the box is read before the prompt is answered");

        // Cancel must leave through the same early exit the unticked case takes.
        int afterShow = show + "PromptForm.ShowModal".Length;
        int nextExit = code.IndexOf("Exit;", afterShow, StringComparison.Ordinal);
        Assert.True(nextExit > 0 && nextExit < guardAt, "dismissing the prompt falls through to the deletion");

        AssertScriptMatchesApp();
    }

    [Fact]
    public void AnUnattendedUninstallNeverBlocksOnThePrompt()
    {
        string code = CodeSection();
        int silent = code.IndexOf("if UninstallSilent then", StringComparison.Ordinal);
        int form = code.IndexOf("CreateCustomForm", StringComparison.Ordinal);
        int dirCheck = code.IndexOf("DirExists(ConfigPath)", StringComparison.Ordinal);

        Assert.True(silent >= 0, "a silent uninstall would show a dialog and hang");
        Assert.True(form > silent, "the prompt is created before the silent case is handled");

        // Nothing to ask about when there is no settings folder: skip the dialog entirely.
        Assert.True(dirCheck >= 0 && dirCheck < form, "the prompt appears even with no settings to keep");

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheUninstallerRemovesTheLogAndTheRunEntry()
    {
        // Inno does not track either, so both need an explicit rule.
        Assert.Contains("Type: files; Name: \"{app}\\trace.log\"", Iss, StringComparison.Ordinal);
        Assert.Contains("ValueName: \"{#RunValueName}\"; Flags: uninsdeletevalue dontcreatekey", Iss, StringComparison.Ordinal);

        AssertScriptMatchesApp();
    }

    [Fact]
    public void TheInstallerScriptIsNotIgnoredByGit()
    {
        string ignore = SourceText.Read(".", ".gitignore");
        string[] lines = ignore.Split('\n').Select(line => line.Trim().TrimEnd('\r')).ToArray();

        // A bare "installer" ignored the folder holding the .iss, so the workflow would
        // have checked out a repository without the script it compiles.
        Assert.DoesNotContain("installer", lines);
        Assert.Contains("installer/Output/", lines);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        int at = text.IndexOf(value, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
