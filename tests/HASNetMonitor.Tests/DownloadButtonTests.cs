using System.Text.RegularExpressions;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The README offers a one-click download button, shown near the top so a visitor sees it
/// before scrolling. The button must point at a URL that exists for every release, and the
/// workflow must attach the stable, version-less asset the README documents. These tests pin
/// both halves so a rename on either side fails here instead of shipping a dead button.
/// </summary>
public class DownloadButtonTests
{
    private const string StableAsset = "HASNetMonitor-Setup.exe";

    /// <summary>The release page, which exists for every release and never 404s.</summary>
    private const string LatestReleaseUrl =
        "https://github.com/hanifalisohag/HASNetMonitor/releases/latest";

    /// <summary>
    /// The direct-download URL the README documents. It only resolves once a release has
    /// shipped with the stable alias, so the button itself must not depend on it.
    /// </summary>
    private const string StableDownloadUrl = LatestReleaseUrl + "/download/" + StableAsset;

    private static string Readme() => SourceText.Read(".", "README.md");

    /// <summary>
    /// The workflow that compiles the installer, found by content so a rename does not
    /// break the test. There must be exactly one, or two builds could publish the same asset.
    /// </summary>
    private static string Workflow()
    {
        string folder = Path.Combine(SourceText.Root(), ".github", "workflows");
        Assert.True(Directory.Exists(folder), $"{folder} does not exist");

        var matches = Directory.GetFiles(folder)
            .Where(f => File.ReadAllText(f).Contains("HASNetMonitor.iss", StringComparison.Ordinal))
            .ToList();

        Assert.True(matches.Count == 1, $"expected one installer workflow, found {matches.Count}");
        return File.ReadAllText(matches[0]);
    }

    [Fact]
    public void TheReadmeButtonLinksTheReleasePage()
    {
        // The button must open a URL that exists for every release. The direct-download URL
        // only resolves after a release carries the stable alias, so pointing the button at
        // it would 404 on any release built before that alias existed.
        string readme = Readme();

        Assert.Matches(
            new Regex(@"<a href=""" + Regex.Escape(LatestReleaseUrl) + @"""\s*>\s*<img"),
            readme);
    }

    [Fact]
    public void TheReadmeDocumentsTheStableDownloadUrl()
    {
        // The stable URL is still offered, as text, for anyone who wants a direct link.
        Assert.Contains(StableDownloadUrl, Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDownloadButtonIsNearTheTop()
    {
        // The point of the button is that a visitor sees it without scrolling, so it must
        // appear in the opening block, below the hero image and before the first section
        // heading.
        string readme = Readme();

        // The button is the anchor that wraps an image; the plain "Latest release" nav link
        // shares the same href but has no image, so match the button form specifically.
        Match buttonMatch = Regex.Match(
            readme,
            @"<a href=""" + Regex.Escape(LatestReleaseUrl) + @"""\s*>\s*<img");
        Assert.True(buttonMatch.Success, "the README has no download button");
        int button = buttonMatch.Index;

        int hero = readme.IndexOf("imgs/hero.png", StringComparison.Ordinal);
        Assert.True(hero >= 0, "the README does not show the hero image");

        int firstSection = readme.IndexOf("\n## ", StringComparison.Ordinal);
        Assert.True(firstSection >= 0, "the README has no section heading");

        Assert.True(button > hero, "the download button should come after the hero image");
        Assert.True(button < firstSection, "the download button should come before the first section");
    }

    [Fact]
    public void TheDownloadButtonIsBigAndGreen()
    {
        // A default shields.io badge is ~20px tall and reads as a label, not a button. The
        // button sets an explicit width so it stands out, but stays modest rather than
        // dominating the page.
        string readme = Readme();

        Match match = Regex.Match(
            readme,
            @"<a href=""" + Regex.Escape(LatestReleaseUrl) + @"""\s*>\s*<img[^>]*src=""([^""]+)""[^>]*width=""(\d+)""",
            RegexOptions.Singleline);

        Assert.True(match.Success, "the README has no large HTML download button");

        int width = int.Parse(match.Groups[2].Value);
        Assert.True(width >= 320, $"the download button is only {width}px wide");
        Assert.True(width <= 440, $"the download button is {width}px wide, too big for the page");

        // The badge colour is the green segment of the shields.io URL.
        string src = match.Groups[1].Value;
        Assert.Contains("22c55e", src, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeNamesTheStableAsset()
    {
        // The asset is version-less, so the README must name it exactly as the workflow
        // attaches it; a versioned name would send the reader to a file that is not there.
        Assert.Contains("HASNetMonitor-Setup.exe", Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowAttachesTheStableAsset()
    {
        string workflow = Workflow();

        // The Setup is built and published under the one stable name, so the README's
        // direct link resolves for every release.
        Assert.Contains("HASNetMonitor-Setup.exe", workflow, StringComparison.Ordinal);

        // Both the create and the replace path must carry it, or a re-run drops the asset.
        Assert.Contains("gh release create $tag $setup", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release upload $tag $setup", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkflowChecksTheStableAssetIsAttached()
    {
        // gh exits 0 for a create that attached nothing, so the asset is read back too.
        string workflow = Workflow();

        Assert.Contains("does not carry the Setup exe", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStableUrlMatchesTheAssetTheWorkflowUploads()
    {
        // The two halves of the documented direct link: the URL the README shows and the
        // file name the workflow attaches. Derive the name from the URL and require the
        // workflow to use it.
        string url = StableDownloadUrl;
        string asset = url[(url.LastIndexOf('/') + 1)..];

        Assert.Equal(StableAsset, asset);
        Assert.Contains($"\"{asset}\"", Workflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeShowsTheStableDownloadUrl()
    {
        // The README's direct link must be the stable, version-less URL, so it never goes
        // stale on a version bump.
        Assert.Contains(StableDownloadUrl, Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeHasNoVersionedDownloadUrl()
    {
        // The versioned URL was replaced by the stable one; a leftover would be a second,
        // stale link a reader could follow to an old build.
        Assert.DoesNotMatch(
            new Regex(@"releases/download/v[0-9][^/\s]*/HASNetMonitor-Setup-[0-9][^/\s]*\.exe"),
            Readme());
    }
}
