using System.Xml.Linq;

namespace HASNetMonitor.Tests;

/// <summary>
/// The README's "Build the installer locally" section is the only place a reader learns how
/// to produce the Setup exe by hand. These tests keep it aligned with the three files it
/// describes: the .iss that is compiled, the workflow that compiles it on CI, and the
/// project version that ends up in the file name.
/// </summary>
public class ReadmeInstallerTests
{
    private const string PublishCommand = "dotnet publish HASNetMonitor.csproj -c Release -f net48 -o publish";

    /// <summary>The one path the workflow searches last, and the one a developer machine uses.</summary>
    private const string IsccPath = @"$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe";

    private static string Readme() => SourceText.Read(".", "README.md");

    private static string Iss() => SourceText.Read("installer", "HASNetMonitor.iss");

    private static string Workflow()
    {
        string folder = Path.Combine(SourceText.Root(), ".github", "workflows");
        string file = Directory.GetFiles(folder)
            .First(f => File.ReadAllText(f).Contains("HASNetMonitor.iss", StringComparison.Ordinal));
        return File.ReadAllText(file);
    }

    private static string ProjectVersion()
    {
        XDocument project = XDocument.Load(Path.Combine(SourceText.Root(), "HASNetMonitor.csproj"));
        string? version = project.Descendants("Version").FirstOrDefault()?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(version), "HASNetMonitor.csproj has no <Version>");
        return version!;
    }

    /// <summary>How many times <paramref name="needle"/> appears, on both test targets.</summary>
    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// The rules that must hold whatever the section says, called at the end of every test
    /// that reads the section. A section that lost its heading, or names two different
    /// compiles, is not the section the individual assertions were written against.
    /// </summary>
    private static void AssertSectionInvariants()
    {
        string section = Section();

        // Exactly one publish and one compile: two would let a reader copy the wrong build.
        // Counted as full commands, so the prose that names ISCC.exe is not a second one.
        Assert.Equal(1, CountOccurrences(section, PublishCommand));
        Assert.Equal(1, CountOccurrences(section, $"& \"{IsccPath}\""));

        // Everything the section tells the reader to work with exists in the repository.
        foreach (string relative in new[] { @"installer\HASNetMonitor.iss", @"installer\config.default.json", "LICENSE" })
        {
            string full = Path.Combine(SourceText.Root(), relative.Replace('\\', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"the section names a missing file: {relative}");
        }

        // The section ends at the next top-level heading, so it cannot swallow the README.
        int heading = Readme().IndexOf("## Build the installer locally", StringComparison.Ordinal);
        Assert.True(
            Readme().Contains("\n## ", heading + 1, StringComparison.Ordinal),
            "the installer section is the last one; Section() would run to the end of the file");
    }

    /// <summary>The section runs to the next top-level heading, like the Designs check does.</summary>
    private static string Section()
    {
        string readme = Readme();
        int start = readme.IndexOf("## Build the installer locally", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no \"Build the installer locally\" section");

        int next = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return next < 0 ? readme[start..] : readme[start..next];
    }

    [Fact]
    public void TheReadmePublishesBeforeItCompilesTheScript()
    {
        // The .iss packs a folder that already exists; the reverse order ships a stale exe.
        string section = Section();

        int publish = section.IndexOf(PublishCommand, StringComparison.Ordinal);
        int compile = section.IndexOf("ISCC.exe", StringComparison.Ordinal);

        Assert.True(publish >= 0, "the section does not give the publish command");
        Assert.True(compile >= 0, "the section does not compile the .iss");
        Assert.True(publish < compile, "the section compiles the script before publishing");

        // The exact command the .iss and the workflow document, not a paraphrase of it.
        Assert.Contains(PublishCommand, Iss(), StringComparison.Ordinal);

        AssertSectionInvariants();
    }

    /// <summary>The lines inside fenced code blocks: commands a reader pastes, not prose.</summary>
    private static IReadOnlyList<string> CodeLines()
    {
        var lines = new List<string>();
        bool fenced = false;

        foreach (string raw in Readme().Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                lines.Add(line.Trim());
            }
        }

        return lines;
    }

    [Fact]
    public void EveryIsccCommandInTheReadmeUsesThePowerShellCallOperator()
    {
        // A quoted path at the start of a PowerShell line is a ParserError, not a program:
        // what was pasted into this repository's own prompt failed that way. Every pasted
        // command that names the compiler must therefore start with the call operator.
        var commands = CodeLines()
            .Where(line => line.Contains("ISCC.exe", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(commands);

        foreach (string line in commands)
        {
            Assert.True(
                line.StartsWith("& ", StringComparison.Ordinal),
                $"a README command names ISCC.exe without the call operator: {line}");
            Assert.DoesNotContain("%LOCALAPPDATA%", line, StringComparison.Ordinal);
        }

        Assert.Contains($"& \"{IsccPath}\" installer\\HASNetMonitor.iss", Readme(), StringComparison.Ordinal);

        AssertSectionInvariants();
    }

    /// <summary>
    /// The three /D switches the section's table offers. A switch the script does not read
    /// would be silently ignored by ISCC, so the reader's override would do nothing.
    /// </summary>
    [Fact]
    public void EveryOverrideTheReadmeOffersIsOneTheScriptReads()
    {
        string section = Section();

        foreach (string name in new[] { "AppVersion", "PublishDir", "OutputDir" })
        {
            Assert.Contains($"/D{name}=", section, StringComparison.Ordinal);

            // Both halves of what ISCC needs: the value has to be consumed by #ifndef/#define.
            Assert.Contains($"#ifndef {name}", Iss(), StringComparison.Ordinal);
            Assert.Contains($"#define {name} ", Iss(), StringComparison.Ordinal);
        }

        // The table must not promise a fourth switch the script never reads.
        Assert.DoesNotContain("/DAppName=", section, StringComparison.Ordinal);
        Assert.DoesNotContain("/DConfigFolder=", section, StringComparison.Ordinal);

        AssertSectionInvariants();
    }

    [Fact]
    public void TheIsccPathTheReadmeGivesIsOneTheWorkflowLooksFor()
    {
        // The documented path is per-user: it is not on PATH, and CI has to find it by
        // probing. A README path outside that list is a path CI would reject.
        Assert.Contains(IsccPath + "\"", Workflow(), StringComparison.Ordinal);
        Assert.Contains(IsccPath, Readme(), StringComparison.Ordinal);

        AssertSectionInvariants();
    }

    [Fact]
    public void TheReadmeNamesTheSetupFileTheScriptProduces()
    {
        // The output name is version-less, so the README must name it exactly as the script
        // writes it; a versioned name sends the reader looking for a file that is not there.
        Assert.Contains("HASNetMonitor-Setup.exe", Section(), StringComparison.Ordinal);

        // The output name is exactly the stable one: a versioned suffix would break the
        // README's direct link, so match the whole line, not a prefix of it.
        string outputLine = Iss()
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.StartsWith("OutputBaseFilename=", StringComparison.Ordinal));
        Assert.Equal("OutputBaseFilename=HASNetMonitor-Setup", outputLine);

        // The version still drives the wizard, so the script keeps reading it from <Version>.
        Assert.Contains($"#define AppVersion \"{ProjectVersion()}\"", Iss(), StringComparison.Ordinal);

        AssertSectionInvariants();
    }

    [Fact]
    public void TheScriptHeaderDocumentsThePowerShellCommand()
    {
        // The header is what someone who opens the .iss directly reads, and it used to carry
        // no PowerShell form at all - the one shell where the cmd.exe line does not work.
        string header = Iss()[..Iss().IndexOf("[Setup]", StringComparison.Ordinal)];

        Assert.Contains("& \"$env:LOCALAPPDATA\\Programs\\Inno Setup 6\\ISCC.exe\"", header, StringComparison.Ordinal);

        // And it still states where the result lands.
        Assert.Contains(@"installer\Output\HASNetMonitor-Setup.exe", header, StringComparison.Ordinal);
    }
}
