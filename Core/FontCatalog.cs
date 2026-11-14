using System.Drawing;
using System.Drawing.Text;

namespace TaskbarNetMonitor.Core;

/// <summary>
/// Which font the widget draws with. The configured name is only ever stored when
/// that family is actually installed, so a config copied from another machine falls
/// back to the automatic taskbar font instead of silently picking something odd.
/// </summary>
internal static class FontCatalog
{
    /// <summary>The config value that means "use the taskbar's own font".</summary>
    public const string Automatic = "";

    /// <summary>Fonts tried in order when no family is configured or it is missing.</summary>
    private static readonly string[] Fallbacks =
    [
        "Segoe UI Variable Text",
        "Segoe UI",
        "Tahoma",
    ];

    private static readonly Lazy<string[]> InstalledNames = new(ReadInstalled);

    /// <summary>Case-insensitive membership test, so every frame does not rescan the list.</summary>
    private static readonly Lazy<HashSet<string>> InstalledLookup =
        new(() => new HashSet<string>(InstalledNames.Value, StringComparer.OrdinalIgnoreCase));

    /// <summary>Every installed family, sorted, for the settings dialog.</summary>
    public static IReadOnlyList<string> InstalledFamilies => InstalledNames.Value;

    public static string Describe(string? family)
        => string.IsNullOrWhiteSpace(family) ? "Automatic (taskbar font)" : family!;

    public static bool IsInstalled(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        return InstalledLookup.Value.Contains(family!.Trim());
    }

    /// <summary>
    /// The configured family if it is installed, otherwise the first available
    /// fallback, otherwise the generic sans-serif. Never throws: this runs on the
    /// UI thread of an always-on widget.
    /// </summary>
    public static string Resolve(string? family)
    {
        if (IsInstalled(family))
        {
            return family!.Trim();
        }

        foreach (string candidate in Fallbacks)
        {
            if (IsInstalled(candidate))
            {
                return candidate;
            }
        }

        return FontFamily.GenericSansSerif.Name;
    }

    /// <summary>Config-level repair: keep the name only when it can be drawn here.</summary>
    public static string Normalize(string? family)
        => IsInstalled(family) ? family!.Trim() : Automatic;

    /// <summary>Bold and/or italic, never the empty style.</summary>
    public static FontStyle StyleFor(bool bold, bool italic)
    {
        FontStyle style = FontStyle.Regular;
        if (bold)
        {
            style |= FontStyle.Bold;
        }

        if (italic)
        {
            style |= FontStyle.Italic;
        }

        return style;
    }

    private static string[] ReadInstalled()
    {
        var names = new List<string>();
        try
        {
            using var collection = new InstalledFontCollection();
            foreach (FontFamily family in collection.Families)
            {
                names.Add(family.Name);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            // A broken font cache must not stop the widget from drawing: the fallback
            // chain plus GenericSansSerif still give a usable font.
            return [];
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. names];
    }
}
