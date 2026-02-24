using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace BacpacGUI.Desktop.ViewModels;

internal sealed class BufferedLogProgress : IProgress<string>, IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Action<string> _appendAction;
    private readonly CancellationTokenSource _pumpCts;
    private readonly Task _pumpTask;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _flushInterval;

    public BufferedLogProgress(Action<string> appendAction, CancellationToken operationToken, int maxBatchSize = 40, int flushIntervalMs = 120)
    {
        _appendAction = appendAction;
        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        _maxBatchSize = Math.Max(1, maxBatchSize);
        _flushInterval = TimeSpan.FromMilliseconds(Math.Max(40, flushIntervalMs));
        _pumpTask = PumpAsync(_pumpCts.Token);
    }

    public void Report(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _queue.Enqueue(value);
    }

    public async ValueTask DisposeAsync()
    {
        _pumpCts.Cancel();

        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Pump cancellation is expected on dispose.
        }
        finally
        {
            await FlushAllAsync().ConfigureAwait(false);
            _pumpCts.Dispose();
        }
    }

    private async Task PumpAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_flushInterval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            FlushQueue();
        }
    }

    private void FlushQueue()
    {
        var batch = new List<string>(_maxBatchSize);
        while (batch.Count < _maxBatchSize && _queue.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var entry in batch)
            {
                _appendAction(entry);
            }
        }, DispatcherPriority.Background);
    }

    private async Task FlushAllAsync()
    {
        while (true)
        {
            var batch = new List<string>(_maxBatchSize);
            while (batch.Count < _maxBatchSize && _queue.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var entry in batch)
                {
                    _appendAction(entry);
                }
            }, DispatcherPriority.Background);
        }
    }
}
