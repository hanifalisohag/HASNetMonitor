using Microsoft.Win32;

namespace HASNetMonitor.Rendering;

/// <summary>
/// Whether the taskbar is currently the dark or the light variant. The taskbar
/// follows "SystemUsesLightTheme", not the app theme.
/// </summary>
internal readonly record struct WidgetTheme(bool IsDark)
{
    public static WidgetTheme Dark { get; } = new(true);

    public static WidgetTheme Light { get; } = new(false);

    public Color TextColor => IsDark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 22, 22, 22);

    public Color TrackColor => IsDark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(56, 0, 0, 0);

    public Color BackdropColor(byte alpha) => IsDark
        ? Color.FromArgb(alpha, 30, 30, 30)
        : Color.FromArgb(alpha, 249, 249, 249);

    public static WidgetTheme FromRegistry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int value)
            {
                return value == 0 ? Dark : Light;
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Dark;
    }
}
