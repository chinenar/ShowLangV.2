using System.Threading;

namespace ShowLangNative;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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

        ApplicationConfiguration.Initialize();
        Application.Run(new LanguageApplicationContext());
        GC.KeepAlive(singleInstance);
    }
}
