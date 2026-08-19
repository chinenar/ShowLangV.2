using System.Threading;

namespace ShowLangNative;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
