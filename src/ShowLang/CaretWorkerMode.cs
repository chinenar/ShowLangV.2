using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;

namespace ShowLangNative;

internal sealed class CaretWorkerRequest
{
    public long RequestId { get; init; }
    public long Window { get; init; }
}

internal sealed class CaretWorkerResponse
{
    public long RequestId { get; init; }
    public bool Success { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? Error { get; init; }

    internal AnchorTarget? ToAnchorTarget()
    {
        if (!Success)
        {
            return null;
        }

        return new AnchorTarget(
            new Rectangle(X, Y, Width, Height),
            AnchorKind.Caret,
            Source);
    }
}

internal static class CaretWorkerMode
{
    internal const string Command = "--caret-worker";
    internal const string ReadyMessage = "SHOWLANG_CARET_WORKER_READY";

    internal static bool TryRun(string[] args)
    {
        if (args.Length != 1
            || !string.Equals(
                args[0],
                Command,
                StringComparison.Ordinal))
        {
            return false;
        }

        Run();
        return true;
    }

    private static void Run()
    {
        using StreamReader input = new(
            Console.OpenStandardInput());
        using StreamWriter output = new(
            Console.OpenStandardOutput())
        {
            AutoFlush = true,
        };

        PreloadAccessibility();
        output.WriteLine(ReadyMessage);

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            CaretWorkerResponse response = Handle(line);
            output.WriteLine(JsonSerializer.Serialize(response));
        }
    }

    private static CaretWorkerResponse Handle(string json)
    {
        CaretWorkerRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<CaretWorkerRequest>(json);
            if (request is null || request.Window == 0)
            {
                throw new InvalidDataException(
                    "The caret request is invalid.");
            }

            IntPtr foreground = new(request.Window);
            AnchorTarget? target =
                CaretLocator.QueryAccessibleTarget(foreground);
            if (target is not AnchorTarget found)
            {
                return new CaretWorkerResponse
                {
                    RequestId = request.RequestId,
                    Success = false,
                    Error = "No accessible caret was found.",
                };
            }

            return new CaretWorkerResponse
            {
                RequestId = request.RequestId,
                Success = true,
                X = found.Bounds.X,
                Y = found.Bounds.Y,
                Width = found.Bounds.Width,
                Height = found.Bounds.Height,
                Source = found.Source,
            };
        }
        catch (Exception exception)
        {
            return new CaretWorkerResponse
            {
                RequestId = request?.RequestId ?? 0,
                Success = false,
                Error = exception.GetType().Name
                    + ": "
                    + exception.Message,
            };
        }
    }

    private static void PreloadAccessibility()
    {
        try
        {
            // Load UI Automation once while the worker starts. This does not
            // inspect the active text caret and keeps the first real request
            // from paying the full assembly/COM initialization cost.
            _ = AutomationElement.RootElement.Current.ProcessId;
        }
        catch
        {
        }
    }
}
