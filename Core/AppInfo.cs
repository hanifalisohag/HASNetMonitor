using System.Reflection;

namespace HASNetMonitor.Core;

/// <summary>
/// The product identity: what the app calls itself, who wrote it, where to write to and
/// which version is running. One place so the About dialog, the tray tooltip and the
/// exe's file properties cannot drift apart.
/// </summary>
internal static class AppInfo
{
    /// <summary>Shown to the user. The exe, config folder and logon entry all use it.</summary>
    public const string Name = "HAS NetMonitor";

    public const string Author = "Md Hanif Ali Sohag";

    public const string ContactEmail = "hanifalisohag@gmail.com";

    /// <summary>
    /// The licence the product ships under. The full text is in LICENSE at the
    /// repository root and is shown in the About box and the installer's licence page,
    /// so this constant, the file and the UI cannot drift apart.
    /// </summary>
    public const string LicenseName = "MIT License";

    /// <summary>
    /// What a redistribution must keep, in the words the About box and the installer
    /// both show. The attribution clause is the point of the licence choice: credit to
    /// the author has to travel with every copy.
    /// </summary>
    public const string LicenseCredit =
        "Must credit Md Hanif Ali Sohag as the original author";

    /// <summary>One-line licence summary, as the About box and installer show it.</summary>
    public static string LicenseSummary => $"{LicenseName} - {LicenseCredit}";


    /// <summary>The running version, e.g. <c>1.0.0</c>, read from the assembly.</summary>
    public static string Version { get; } = ReadVersion(typeof(AppInfo).Assembly);

    /// <summary>
    /// The informational version without the source-revision suffix the SDK appends
    /// (<c>1.2.3+abcdef</c>), falling back to the assembly version when the attribute is
    /// absent. Never empty: an unreadable assembly yields <c>0.0.0</c>.
    /// </summary>
    public static string ReadVersion(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string? cleaned = TrimBuildMetadata(informational);
        if (cleaned is not null)
        {
            return cleaned;
        }

        Version? declared = assembly.GetName().Version;
        if (declared is not null)
        {
            return $"{declared.Major}.{declared.Minor}.{Math.Max(declared.Build, 0)}";
        }

        return "0.0.0";
    }

    /// <summary>Drops the <c>+commit</c> part and returns null when nothing is left.</summary>
    public static string? TrimBuildMetadata(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        // The `!` is for .NET Framework, whose string.IsNullOrWhiteSpace carries no
        // null-state annotation, so the compiler cannot see the null check above.
        string trimmed = version!.Trim();
        int plus = trimmed.IndexOf('+');
        if (plus >= 0)
        {
            trimmed = trimmed.Substring(0, plus).Trim();
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>"HAS NetMonitor 1.0.0", as the dialog shows it.</summary>
    public static string Title => $"{Name} {Version}";
}
