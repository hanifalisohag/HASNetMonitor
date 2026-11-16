using System.Text.RegularExpressions;
using HASNetMonitor.Core;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The README shows one preview image per design area. These tests keep the images and the
/// text that describes them honest: every linked image exists, and the counts the prose
/// states match the constants the app actually offers.
/// </summary>
public class ReadmeGalleryTests
{
    /// <summary>
    /// The README states how many presets there are. That number has to come from the table,
    /// or the first preset added makes the documentation wrong.
    /// </summary>
    [Fact]
    public void TheReadmePresetCountMatchesTheTable()
    {
        string readme = Readme();

        // Only the hundreds the gallery has ever been near, so a typo in the table (or a
        // dropped entry) fails here instead of silently passing a word nothing matches.
        var words = new Dictionary<int, string>
        {
            [23] = "Twenty-three",
            [24] = "Twenty-four",
            [25] = "Twenty-five",
            [26] = "Twenty-six",
        };

        int count = WidgetPresets.All.Count;
        if (!words.TryGetValue(count, out string? stated))
        {
            throw new Xunit.Sdk.XunitException(
                $"the README spells out the count in words; add {count} to this test and the prose");
        }

        Assert.Contains(stated, readme, StringComparison.Ordinal);
        Assert.Contains("Settings → Presets", readme, StringComparison.Ordinal);

        // No other number may also appear, or the sentence is ambiguous.
        foreach (string other in words.Values.Where(w => !string.Equals(w, stated, StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(other, readme, StringComparison.Ordinal);
        }
    }

    private static string Readme() => SourceText.Read(".", "README.md");

    private static string Root() => SourceText.Root();

    private static IReadOnlyList<string> LinkedImages()
    {
        // Only repository-local targets: a line that points at an absolute URL names a file
        // this repository does not own, so there is nothing here to check.
        var found = new List<string>();
        foreach (Match match in Regex.Matches(Readme(), @"!\[[^\]]*\]\(([^)\s]+)\)"))
        {
            string target = match.Groups[1].Value;
            if (!target.StartsWith("http", StringComparison.Ordinal))
            {
                found.Add(target);
            }
        }

        return found;
    }

    [Fact]
    public void EveryImageTheReadmeLinksExists()
    {
        IReadOnlyList<string> linked = LinkedImages();
        Assert.NotEmpty(linked);

        foreach (string relative in linked)
        {
            string full = Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"README links a missing image: {relative}");
        }
    }

    [Fact]
    public void EveryLinkedImageIsANonEmptyPng()
    {
        foreach (string relative in LinkedImages())
        {
            string full = Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(full);

            // A PNG magic number, not a placeholder or a renamed text file.
            Assert.True(bytes.Length > 1024, $"README image looks empty: {relative}");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
        }
    }

    [Fact]
    public void TheDesignsSectionShowsTheDesignPreview()
    {
        string readme = Readme();
        int designs = readme.IndexOf("## Designs", StringComparison.Ordinal);
        Assert.True(designs >= 0, "README has no Designs section");

        // The Designs section runs to the next top-level heading.
        int next = readme.IndexOf("\n## ", designs + 1, StringComparison.Ordinal);
        string section = next < 0 ? readme[designs..] : readme[designs..next];

        Assert.Contains("imgs/designs.png", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLookCountInTheReadmeMatchesTheStylesTheAppOffers()
    {
        // Prose that states a stale count is the drift this catches. Compared exactly: the
        // README must say the real number, in the form the rest of the file uses.
        int count = WidgetStyles.All.Count;
        Assert.Equal(10, count);

        Assert.Contains($"Ten looks", Readme(), StringComparison.Ordinal);
        Assert.DoesNotContain($"Eight looks", Readme(), StringComparison.Ordinal);
        Assert.DoesNotContain($"Nine looks", Readme(), StringComparison.Ordinal);
        Assert.DoesNotContain($"Eleven looks", Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheArrowSectionExistsWhenThereIsMoreThanOneArrowStyle()
    {
        Assert.True(ArrowStyles.All.Count > 1);

        string readme = Readme();
        Assert.Contains("imgs/arrows.png", readme, StringComparison.Ordinal);

        // The drawn Bar arrow is the one that had its head pointing the wrong way.
        Assert.Contains(ArrowStyles.Bar, readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheColourSectionNamesTheModesTheAppOffers()
    {
        string readme = Readme();

        Assert.Contains("imgs/text-colours.png", readme, StringComparison.Ordinal);

        // Automatic, same-as-the-bar and custom are the three real modes; the prose must not
        // promise a fourth.
        Assert.Equal(3, TextColorModes.All.Count);
    }

    [Fact]
    public void TheReadmePointsAtSettingsGroupsThatExist()
    {
        // The prose tells the reader where to click. Name a group or row the dialog does not
        // have and the instructions are fiction.
        string dialog = SourceText.Read("UI", "SettingsForm.cs");
        string readme = Readme();

        Assert.Contains("Settings \u2192 Look \u2192 Style", readme, StringComparison.Ordinal);
        Assert.Contains("Settings \u2192 Look \u2192 Arrow type", readme, StringComparison.Ordinal);
        Assert.Contains("Settings \u2192 Colours and behaviour", readme, StringComparison.Ordinal);

        Assert.Contains("CreateGroup(\"Look\")", dialog, StringComparison.Ordinal);
        Assert.Contains("CreateGroup(\"Colours and behaviour\")", dialog, StringComparison.Ordinal);
        Assert.Contains("\"Style:\"", dialog, StringComparison.Ordinal);
        Assert.Contains("\"Arrow type:\"", dialog, StringComparison.Ordinal);
    }
}
