namespace HASNetMonitor.Tests;

/// <summary>
/// The one string helper the test sources need and no target has: "does
/// <paramref name="value"/> occur at or after <paramref name="startIndex"/>".
/// <see cref="string.IndexOf(string, int, StringComparison)"/> answers it, but the
/// question reads as a containment check, and the tests that scan a section of the
/// README ask it in those words.
/// </summary>
internal static class StringExtensions
{
    public static bool Contains(this string text, string value, int startIndex, StringComparison comparison)
        => text.IndexOf(value, startIndex, comparison) >= 0;
}
