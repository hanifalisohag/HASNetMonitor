using System.Text;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\HASNetMonitor.Instance";

    [STAThread]
    private static int Main(string[] args)
    {
#if NETFRAMEWORK
        // .NET Framework has no ApplicationConfiguration: the same two calls it
        // makes are the classic WinForms start-up pair. DPI awareness comes from
        // App.config for this target.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif

        if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTest();
        }

        int smokeSeconds = ParseSmokeSeconds(args);

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Already running: a second widget would fight the first one for the same spot.
            return 0;
        }

        using var host = new MonitorHost();

        if (smokeSeconds > 0)
        {
            var stopTimer = new System.Windows.Forms.Timer { Interval = smokeSeconds * 1000 };
            stopTimer.Tick += (_, _) =>
            {
                stopTimer.Stop();
                stopTimer.Dispose();
                host.Exit();
            };
            stopTimer.Start();
        }

        Application.Run(host);
        return 0;
    }

    private static int RunSelfTest()
    {
        TextWriter writer;
        StreamWriter? ownedWriter = null;
        try
        {
            // A WinExe has no console of its own; borrow the caller's so the report is visible.
            if (NativeMethods.AttachConsole(-1) || NativeMethods.AllocConsole())
            {
                var stream = Console.OpenStandardOutput();
                ownedWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                writer = ownedWriter;
                Console.SetOut(writer);
            }
            else
            {
                writer = Console.Out;
            }
        }
        catch (IOException)
        {
            writer = Console.Out;
        }

        try
        {
            return SelfTest.Run(writer) ? 0 : 1;
        }
        finally
        {
            ownedWriter?.Flush();
            ownedWriter?.Dispose();
        }
    }

    private static int ParseSmokeSeconds(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--smoke", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int seconds)
                && seconds > 0)
            {
                return Math.Min(seconds, 600);
            }
        }

        return 0;
    }
}
