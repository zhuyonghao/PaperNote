using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PaperNote.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string[]> _pending = new();
    private readonly object _gate = new();
    private Action<string[]>? _handler;
    public bool IsPrimary { get; }
    public SingleInstanceService(string scope)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..20];
        _pipeName = "PaperNote-" + id;
        _mutex = new Mutex(false, "Global\\" + _pipeName);
        try { IsPrimary = _mutex.WaitOne(0); } catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary) _ = ListenAsync();
    }
    public void SetHandler(Action<string[]> handler)
    {
        lock (_gate) { _handler = handler; while (_pending.TryDequeue(out var args)) handler(args); }
    }
    public async Task<bool> SendAsync(string[] args)
    {
        if (!LaunchRequest.TryParse(args, out var request)) return false;
        var message = JsonSerializer.Serialize(request.ToArguments());
        if (message.Length >= LaunchRequest.MaximumMessageLength) return false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(message);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { return false; }
    }
    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe);
                // Only bounded, validated action names and document paths cross this endpoint.
                var buffer = new char[LaunchRequest.MaximumMessageLength]; var length = 0;
                while (length < buffer.Length)
                {
                    var n = await reader.ReadAsync(buffer.AsMemory(length, 1), _stop.Token);
                    if (n == 0 || buffer[length] == '\n') break;
                    length += n;
                }
                if (length == buffer.Length) continue;
                var args = JsonSerializer.Deserialize<string[]>(new string(buffer, 0, length));
                if (args is null || !LaunchRequest.TryParse(args, out var request)) continue;
                var normalized = request.ToArguments();
                lock (_gate) { if (_handler is { } handler) handler(normalized); else _pending.Enqueue(normalized); }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                try { await Task.Delay(100, _stop.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }
    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose(); _stop.Dispose();
    }
}
