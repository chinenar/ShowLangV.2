using System.IO;

namespace ShowLangNative;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShowLang");

    private static readonly string LogPath = Path.Combine(
        LogDirectory,
        "showlang.log");

    internal static void Write(Exception exception)
    {
        Write(exception.ToString());
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTimeOffset.Now:O}] {message}\r\n");
            }
        }
        catch
        {
            // Logging must never interrupt the overlay.
        }
    }
}
