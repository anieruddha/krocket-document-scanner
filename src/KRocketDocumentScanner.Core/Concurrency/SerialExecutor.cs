using System.Collections.Concurrent;

namespace KRocketDocumentScanner.Core.Concurrency;

public sealed class SerialExecutor : IDisposable
{
    private readonly BlockingCollection<Func<Task>> _queue = new();
    private readonly Thread _worker;
    private volatile bool _disposed;

    public SerialExecutor(string? threadName = null)
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = threadName ?? "serial-executor",
        };
        _worker.Start();
    }

    public int WorkerManagedThreadId => _worker.ManagedThreadId;

    private void WorkerLoop()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Add(async () =>
        {
            try
            {
                var result = await action(ct).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    public async IAsyncEnumerable<T> RunStreamAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var channel = System.Threading.Channels.Channel.CreateUnbounded<T>();

        _queue.Add(async () =>
        {
            try
            {
                await foreach (var item in source(ct).WithCancellation(ct).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                }
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
