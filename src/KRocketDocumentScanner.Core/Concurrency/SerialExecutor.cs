using System.Collections.Concurrent;

namespace KRocketDocumentScanner.Core.Concurrency;

/// <summary>
/// Runs work items one at a time on a single, dedicated, long-lived background thread —
/// regardless of which caller thread submits them.
///
/// Why this exists: a prior implementation called into the scanning library from a fresh
/// ad-hoc thread per UI action (one for "list devices", another for "preview", another for
/// "scan", etc.). SANE's network backend uses avahi/mDNS for network scanner discovery, and
/// the underlying native avahi client is not safe to drive from multiple threads — even
/// non-overlapping calls from *different* threads corrupted its internal state and crashed
/// the process. Routing every scanner-engine call through one persistent thread, via this
/// class, fixes that at the source: it doesn't matter which thread calls
/// <see cref="RunAsync{T}"/> — the actual work always executes on the same worker thread.
///
/// This is intentionally a small, dependency-free, directly testable primitive — see
/// KRocketDocumentScanner.Core.Tests / the verification harness for concurrency tests exercising it from
/// multiple simultaneous caller threads.
/// </summary>
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

    /// <summary>The physical OS thread ID every submitted action actually runs on. Exposed for testing/diagnostics.</summary>
    public int WorkerManagedThreadId => _worker.ManagedThreadId;

    private void WorkerLoop()
    {
        // Deliberately NOT passing _cts.Token here: BlockingCollection.CompleteAdding()
        // (called from Dispose) already makes GetConsumingEnumerable end the loop
        // gracefully once the queue drains. Passing a cancellation token instead would
        // make it THROW OperationCanceledException on an unmonitored background thread,
        // which crashes the whole process rather than shutting down cleanly.
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            // Each queued delegate is responsible for completing its own TaskCompletionSource;
            // we just run it to completion synchronously on this one thread before taking the
            // next item, which is exactly the "never overlapping" guarantee we need.
            try
            {
                work().GetAwaiter().GetResult();
            }
            catch
            {
                // Exceptions are already captured onto the caller's TaskCompletionSource inside
                // the delegate (see RunAsync/RunStreamAsync below) — nothing to do here except
                // keep the worker loop alive for the next item.
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> on the dedicated worker thread and returns its result.</summary>
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

    /// <summary>
    /// Runs an async-enumerable-producing action on the worker thread, relaying each item back
    /// to the caller through a channel so the caller can consume it as a normal async stream —
    /// while every bit of the actual iteration still happens on the single worker thread.
    /// </summary>
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
