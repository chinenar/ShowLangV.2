using System.Threading;

namespace ShowLangNative;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Establish PerMonitorV2 before worker/probe code touches UIA.
        // Otherwise the main tray is per-monitor aware but the caret worker
        // remains system-DPI aware and returns virtualized coordinates on
        // secondary monitors with a different scale/orientation.
        ApplicationConfiguration.Initialize();

        if (CaretWorkerMode.TryRun(args))
        {
            // The worker owns no tray UI and exits with its parent pipe.
            Environment.Exit(0);
            return;
        }
        if (CaretProbeMode.TryRun(args))
        {
            // UI Automation can create COM worker threads that outlive Main.
            // Probe mode is a disposable isolation process, so terminate it
            // explicitly after the result file has been written.
            Environment.Exit(0);
            return;
        }

        using Mutex singleInstance = new(
            initiallyOwned: true,
            name: @"Local\ShowLang.Native.SingleInstance",
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            return;
        }

        Application.Run(new LanguageApplicationContext());
        GC.KeepAlive(singleInstance);
    }
}
