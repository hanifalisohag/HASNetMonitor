using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// Reads the product sources from the test run so a rule can be asserted about code
/// that is otherwise only reachable on a real desktop.
/// </summary>
internal static class SourceText
{
    public static string Read(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, folder, file);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"could not find {folder}/{file} above {AppContext.BaseDirectory}");
    }

    public static string ExtractMethod(string folder, string file, string signature)
    {
        string source = Read(folder, file);
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found in {file}: {signature}");

        int next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    /// <summary>The repository root, found above the test binary, so a whole tree can be scanned.</summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HASNetMonitor.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"no HASNetMonitor.csproj above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// Every product and test source file, skipping generated output. Used to assert a rule
    /// that has to hold across the tree rather than in one named file.
    /// </summary>
    public static IReadOnlyList<string> AllSources()
    {
        string root = Root();
        var files = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(file);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
