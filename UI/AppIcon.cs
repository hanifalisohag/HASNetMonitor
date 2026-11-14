using System.Drawing;
using System.Reflection;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// The application mark: the green-download / red-upload arrows from
/// Assets/HASNetMonitor.ico, embedded in the assembly. This is the only definition
/// of the mark - the exe icon, the tray icon and the settings dialog all use it.
/// </summary>
public static class AppIcon
{
    private const string ResourceName = "HASNetMonitor.AppIcon.ico";

    private static readonly Lazy<byte[]> Data = new(ReadResource);

    /// <summary>
    /// The mark at exactly <paramref name="edge"/> pixels square: the frame the shell
    /// asks for when it is present, otherwise the nearest one scaled. Every surface that
    /// shows the app - Explorer, the tray, the settings dialog - goes through this, so
    /// there is only one drawing to keep right.
    /// </summary>
    public static Icon? LoadForSize(int edge)
    {
        byte[] bytes = Data.Value;
        if (bytes.Length == 0 || edge <= 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Icon(stream, edge, edge);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

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
