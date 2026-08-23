using System.IO;

namespace ShowLangNative;

internal static class AppLog
{
    private const long MaxLogBytes = 4L * 1024L * 1024L;
    private const int DuplicateExceptionWindowMilliseconds = 60_000;
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShowLang");

    private static readonly string LogPath = Path.Combine(
        LogDirectory,
        "showlang.log");

    private static string? _lastExceptionSignature;
    private static long _lastExceptionAt;

    internal static void Write(Exception exception)
    {
        string signature = exception.GetType().FullName
            + "|"
            + exception.Message;

        try
        {
            lock (Gate)
            {
                long now = Environment.TickCount64;
                if (string.Equals(
                        signature,
                        _lastExceptionSignature,
                        StringComparison.Ordinal)
                    && now - _lastExceptionAt
                        < DuplicateExceptionWindowMilliseconds)
                {
                    return;
                }

                _lastExceptionSignature = signature;
                _lastExceptionAt = now;
                WriteCore(exception.ToString());
            }
        }
        catch
        {
            // Logging must never interrupt the overlay.
        }
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                WriteCore(message);
            }
        }
        catch
        {
            // Logging must never interrupt the overlay.
        }
    }

    private static void WriteCore(string message)
    {
        Directory.CreateDirectory(LogDirectory);
        RotateIfNeeded();
        File.AppendAllText(
            LogPath,
            $"[{DateTimeOffset.Now:O}] {message}\r\n");
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)
            || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        string previousPath = Path.Combine(
            LogDirectory,
            "showlang.previous.log");
        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.Move(LogPath, previousPath);
    }
}
