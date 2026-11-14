using Xunit;

namespace TaskbarNetMonitor.Tests;

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
}
