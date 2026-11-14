using System.Drawing;
using System.Reflection;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// The application mark: the same green-download / red-upload arrows the tray icon
/// draws, shipped as Assets/TaskbarNetMonitor.ico and embedded in the assembly.
/// </summary>
public static class AppIcon
{
    private const string ResourceName = "TaskbarNetMonitor.AppIcon.ico";

    private static readonly Lazy<byte[]> Data = new(ReadResource);

    /// <summary>
    /// A fresh icon the caller owns, or null when the resource is missing so a
    /// missing decoration can never stop the app from running.
    /// </summary>
    public static Icon? Load()
    {
        byte[] bytes = Data.Value;
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Icon(stream);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static byte[] ReadResource()
    {
        Assembly assembly = typeof(AppIcon).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
