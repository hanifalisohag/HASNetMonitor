#if NETFRAMEWORK
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HASNetMonitor.Core;

/// <summary>
/// The handful of things .NET Framework lacks that the rest of the code uses without
/// thinking. Everything here is compiled only for the <c>net48</c> target, so the modern
/// target keeps using the real APIs.
/// </summary>
internal static class Compat
{
    /// <summary>The running executable, the same answer <c>Environment.ProcessPath</c> gives.</summary>
    public static string? ProcessPath()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The time since <paramref name="timestamp"/>, measured with
    /// <see cref="Stopwatch.GetTimestamp"/>. Matches <c>Stopwatch.GetElapsedTime</c>.
    /// </summary>
    public static TimeSpan GetElapsedTime(long timestamp)
    {
        long elapsed = Stopwatch.GetTimestamp() - timestamp;
        return TimeSpan.FromSeconds(elapsed / (double)Stopwatch.Frequency);
    }

    private const int LoadLibrarySearchDefaultDirs = 0x00001000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, int flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    /// <summary>
    /// Resolves an exported function the way <c>NativeLibrary.TryGetExport</c> does. The
    /// module is deliberately left loaded: the returned pointer points into it and the
    /// process uses these system libraries for its whole lifetime anyway.
    /// </summary>
    public static bool TryGetExport(string fileName, string entryPoint, out IntPtr address)
    {
        address = IntPtr.Zero;

        IntPtr module = LoadLibraryEx(fileName, IntPtr.Zero, LoadLibrarySearchDefaultDirs);
        if (module == IntPtr.Zero)
        {
            return false;
        }

        address = GetProcAddress(module, entryPoint);
        return address != IntPtr.Zero;
    }

    /// <summary>The assembly the app was loaded from, for the embedded icon.</summary>
    public static Assembly AppAssembly => typeof(Compat).Assembly;
}
#endif
