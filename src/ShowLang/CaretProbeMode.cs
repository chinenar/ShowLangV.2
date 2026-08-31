using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ShowLangNative;

internal sealed class CaretProbePayload
{
    public bool Success { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Error { get; init; }

    internal AnchorTarget ToAnchorTarget()
    {
        return new AnchorTarget(
            new Rectangle(X, Y, Width, Height),
            AnchorKind.Caret,
            Source);
    }
}

internal static class CaretProbeMode
{
    internal const string Command = "--caret-probe";

    internal static bool TryRun(string[] args)
    {
        if (args.Length < 3
            || !string.Equals(
                args[0],
                Command,
                StringComparison.Ordinal))
        {
            return false;
        }

        Run(args[1], args[2]);
        return true;
    }

    private static void Run(string windowValue, string outputPath)
    {
        CaretProbePayload payload;
        try
        {
            long rawWindow = long.Parse(
                windowValue,
                CultureInfo.InvariantCulture);
            IntPtr foreground = new(rawWindow);
            AnchorTarget? target =
                CaretLocator.QueryAccessibleTarget(foreground);

            payload = target is AnchorTarget found
                ? new CaretProbePayload
                {
                    Success = true,
                    X = found.Bounds.X,
                    Y = found.Bounds.Y,
                    Width = found.Bounds.Width,
                    Height = found.Bounds.Height,
                    Source = found.Source,
                }
                : new CaretProbePayload
                {
                    Success = false,
                    Error = "No accessible caret was found.",
                };
        }
        catch (Exception exception)
        {
            payload = new CaretProbePayload
            {
                Success = false,
                Error = exception.GetType().Name
                    + ": "
                    + exception.Message,
            };
        }

        try
        {
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                outputPath,
                JsonSerializer.Serialize(payload));
        }
        catch
        {
            // A missing result is treated as a failed probe by the parent.
        }
    }
}
