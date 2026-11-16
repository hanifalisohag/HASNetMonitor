using System.Reflection;

namespace HASNetMonitor.Core;

/// <summary>
/// The full licence text, read from the copy embedded in the assembly so the About box
/// can show it no matter where the exe was installed from. The embedded resource is
/// LICENSE at the repository root (see HASNetMonitor.csproj), which is therefore the one
/// place the wording lives.
/// </summary>
internal static class LicenseInfo
{
    /// <summary>The resource name <c>HASNetMonitor.csproj</c> gives the LICENSE file.</summary>
    public const string ResourceName = "HASNetMonitor.LICENSE";

    /// <summary>
    /// The embedded licence text, or an empty string when it is missing. A missing
    /// resource must not stop the About box from opening or the app from starting.
    /// </summary>
    public static string Text => Read(typeof(LicenseInfo).Assembly);

    /// <summary>
    /// Reads the embedded licence text from <paramref name="assembly"/>, trimming the
    /// byte-order mark a Windows editor may have saved. Null-safe and never throws.
    /// </summary>
    public static string Read(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimStart('\uFEFF').Replace("\r\n", "\n").TrimEnd();
    }

    /// <summary>
    /// True when <paramref name="text"/> is the project's licence: it names the author,
    /// which is the attribution the licence requires, and carries the MIT grant. Used by
    /// the tests so a silently emptied or replaced LICENSE fails the build's checks.
    /// </summary>
    public static bool IsTheProjectLicense(string text)
        => !string.IsNullOrWhiteSpace(text)
           && text.IndexOf(AppInfo.Author, StringComparison.Ordinal) >= 0
           && text.IndexOf("MIT License", StringComparison.Ordinal) >= 0
           && text.IndexOf("Permission is hereby granted", StringComparison.Ordinal) >= 0;
}
