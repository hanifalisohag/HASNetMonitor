using System.Drawing;

namespace HASNetMonitor.Core;

/// <summary>
/// Where the upload and download figures take their colour from. Kept as a named mode
/// rather than only a colour so "follow the taskbar" and "follow the bar" stay possible
/// after the user has picked a custom colour once.
/// </summary>
public static class TextColorModes
{
    /// <summary>The taskbar's own text colour: white on a dark taskbar, near black on a light one.</summary>
    public const string Auto = "Auto";

    /// <summary>Whatever colour that row's bar is drawn in.</summary>
    public const string Bar = "Bar";

    /// <summary>A colour the user picked.</summary>
    public const string Custom = "Custom";

    /// <summary>Every selectable mode, in the order the settings list shows them.</summary>
    public static IReadOnlyList<string> All { get; } = [Auto, Bar, Custom];

    /// <summary>The mode's own name, or <see cref="Auto"/> for anything unknown.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Auto;
        }

        string wanted = value!.Trim();
        foreach (string mode in All)
        {
            if (string.Equals(mode, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return Auto;
    }

    /// <summary>A short label for the settings list.</summary>
    public static string Describe(string? value) => Normalize(value) switch
    {
        Bar => "Same as the bar",
        Custom => "Custom colour",
        _ => "Automatic (taskbar)",
    };

    /// <summary>
    /// The colour a row's figure is drawn in. <paramref name="themeText"/> is the taskbar's
    /// own text colour and <paramref name="barColor"/> the row's fill; a custom value that
    /// cannot be read falls back to the theme rather than to an invisible colour.
    /// </summary>
    public static Color Resolve(string? mode, string? hex, Color barColor, Color themeText)
    {
        return Normalize(mode) switch
        {
            Bar => barColor,
            Custom => ColorHelper.TryParse(hex, out Color custom) ? custom : themeText,
            _ => themeText,
        };
    }
}
