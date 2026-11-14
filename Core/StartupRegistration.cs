using Microsoft.Win32;

namespace TaskbarNetMonitor.Core;

/// <summary>
/// "Run at logon" support. A taskbar widget cannot be a Windows *service*: services
/// run in session 0 with no desktop, so they can never draw on a user's taskbar.
/// The supported equivalent is a per-user autostart entry, which is what this writes.
/// </summary>
internal static class StartupRegistration
{
    public const string ValueName = "TaskbarNetMonitor";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Quoted so paths containing spaces still launch correctly.</summary>
    public static string BuildCommand(string executablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePath);

        string trimmed = executablePath.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("The executable path must not be empty.", nameof(executablePath));
        }

        return $"\"{trimmed}\"";
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, string? executablePath = null)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                string path = executablePath ?? Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                key.SetValue(ValueName, BuildCommand(path), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
