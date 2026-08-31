using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ShowLangNative;

internal sealed class CaretWorkerClient : IDisposable
{
    private const int StartupTimeoutMilliseconds = 1_200;
    private const int QueryTimeoutMilliseconds = 180;

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private CancellationTokenSource? _lifetime;
    private WorkerConnection? _worker;
    private bool _enabled;
    private bool _disposed;
    private int _generation;
    private long _nextRequestId;

    internal void Start()
    {
        lock (_stateGate)
        {
            if (_disposed || _enabled)
            {
                return;
            }

            _enabled = true;
            _generation++;
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        _ = WarmUpAsync();
    }

    internal void Stop()
    {
        WorkerConnection? worker;
        CancellationTokenSource? lifetime;
        lock (_stateGate)
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            _generation++;
            lifetime = _lifetime;
            _lifetime = null;
            worker = _worker;
            _worker = null;
        }

        lifetime?.Cancel();
        lifetime?.Dispose();
        worker?.Dispose();
    }

    internal async Task<AnchorTarget?> QueryAsync(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero
            || !TryGetLifetime(out WorkerSnapshot snapshot))
        {
            return null;
        }

        try
        {
            await _operationGate.WaitAsync(snapshot.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            WorkerConnection? worker = await EnsureWorkerAsync(snapshot)
                .ConfigureAwait(false);
            if (worker is null)
            {
                return null;
            }

            long requestId = Interlocked.Increment(
                ref _nextRequestId);
            CaretWorkerRequest request = new()
            {
                RequestId = requestId,
                Window = foreground.ToInt64(),
            };

            string json = JsonSerializer.Serialize(request);
            await worker.Writer.WriteLineAsync(json)
                .ConfigureAwait(false);

            string? responseLine = await worker.Reader.ReadLineAsync()
                .WaitAsync(
                    TimeSpan.FromMilliseconds(QueryTimeoutMilliseconds),
                    snapshot.Token)
                .ConfigureAwait(false);
            if (responseLine is null)
            {
                throw new EndOfStreamException(
                    "The caret worker closed its output stream.");
            }

            CaretWorkerResponse? response =
                JsonSerializer.Deserialize<CaretWorkerResponse>(
                    responseLine);
            if (response is null
                || response.RequestId != requestId)
            {
                throw new InvalidDataException(
                    "The caret worker returned an invalid response.");
            }

            return response.ToAnchorTarget();
        }
        catch (TimeoutException)
        {
            RestartWorker(snapshot.Generation);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
            or ObjectDisposedException
            or InvalidDataException
            or JsonException)
        {
            AppLog.Write(exception);
            RestartWorker(snapshot.Generation);
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task WarmUpAsync()
    {
        if (!TryGetLifetime(out WorkerSnapshot snapshot))
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync(snapshot.Token)
                .ConfigureAwait(false);
            try
            {
                _ = await EnsureWorkerAsync(snapshot)
                    .ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    private async Task<WorkerConnection?> EnsureWorkerAsync(
        WorkerSnapshot snapshot)
    {
        WorkerConnection? existing;
        lock (_stateGate)
        {
            if (!IsCurrentLocked(snapshot))
            {
                return null;
            }

            existing = _worker;
            if (existing is not null && existing.IsAlive)
            {
                return existing;
            }

            _worker = null;
        }

        existing?.Dispose();
        return await StartWorkerAsync(snapshot)
            .ConfigureAwait(false);
    }

    private async Task<WorkerConnection?> StartWorkerAsync(
        WorkerSnapshot snapshot)
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(CaretWorkerMode.Command);

        Process process = new()
        {
            StartInfo = startInfo,
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return null;
            }

            string? ready = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(
                    TimeSpan.FromMilliseconds(StartupTimeoutMilliseconds),
                    snapshot.Token)
                .ConfigureAwait(false);
            if (!string.Equals(
                    ready,
                    CaretWorkerMode.ReadyMessage,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The caret worker did not become ready.");
            }

            WorkerConnection connection = new(process);
            lock (_stateGate)
            {
                if (!IsCurrentLocked(snapshot))
                {
                    connection.Dispose();
                    return null;
                }

                _worker = connection;
                return connection;
            }
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            process.Dispose();
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidDataException
            or TimeoutException)
        {
            AppLog.Write(exception);
            KillProcess(process);
            process.Dispose();
            return null;
        }
    }

    private bool TryGetLifetime(out WorkerSnapshot snapshot)
    {
        lock (_stateGate)
        {
            if (_disposed
                || !_enabled
                || _lifetime is null)
            {
                snapshot = default;
                return false;
            }

            snapshot = new WorkerSnapshot(
                _generation,
                _lifetime.Token);
            return true;
        }
    }

    private bool IsCurrentLocked(WorkerSnapshot snapshot)
    {
        return !_disposed
            && _enabled
            && _generation == snapshot.Generation
            && _lifetime is not null
            && !_lifetime.IsCancellationRequested;
    }

    private void RestartWorker(int generation)
    {
        WorkerConnection? worker;
        bool restart;
        lock (_stateGate)
        {
            if (_disposed || _generation != generation)
            {
                return;
            }

            worker = _worker;
            _worker = null;
            restart = _enabled;
        }

        worker?.Dispose();
        if (restart)
        {
            _ = WarmUpAsync();
        }
    }

    public void Dispose()
    {
        WorkerConnection? worker;
        CancellationTokenSource? lifetime;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _enabled = false;
            _generation++;
            lifetime = _lifetime;
            _lifetime = null;
            worker = _worker;
            _worker = null;
        }

        lifetime?.Cancel();
        lifetime?.Dispose();
        worker?.Dispose();
        _operationGate.Dispose();
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(500);
            }
        }
        catch
        {
        }
    }

    private readonly record struct WorkerSnapshot(
        int Generation,
        CancellationToken Token);

    private sealed class WorkerConnection : IDisposable
    {
        internal WorkerConnection(Process process)
        {
            Process = process;
            Reader = process.StandardOutput;
            Writer = process.StandardInput;
            Writer.AutoFlush = true;
        }

        internal Process Process { get; }
        internal StreamReader Reader { get; }
        internal StreamWriter Writer { get; }

        internal bool IsAlive
        {
            get
            {
                try
                {
                    return !Process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                Writer.Dispose();
            }
            catch
            {
            }

            try
            {
                Reader.Dispose();
            }
            catch
            {
            }

            KillProcess(Process);
            Process.Dispose();
        }
    }
}
