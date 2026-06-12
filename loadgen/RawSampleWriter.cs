using System.Globalization;
using System.Threading.Channels;

namespace Bmt.LoadGen;

/// <summary>
/// Lock-free batched CSV writer for sampled operation records. Producers enqueue rows from many
/// worker threads; a single background task drains and flushes them to disk.
/// Format: <c>ts_us,op,latency_us,success,err</c>.
/// </summary>
public sealed class RawSampleWriter : IAsyncDisposable
{
    private readonly Channel<string> _channel;
    private readonly StreamWriter _writer;
    private readonly Task _drainTask;
    private bool _disposed;

    public RawSampleWriter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _writer = new StreamWriter(new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1 << 16))
        {
            AutoFlush = false,
        };
        _writer.WriteLine("ts_us,op,latency_us,success,err");

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _drainTask = Task.Run(DrainAsync);
    }

    /// <summary>Enqueues one sampled record. Non-blocking; drops nothing under normal load.</summary>
    public void Write(long tsUs, string op, long latencyUs, bool success, string? err)
    {
        // Keep the err field CSV-safe: strip commas and newlines.
        string safeErr = string.IsNullOrEmpty(err)
            ? string.Empty
            : err.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');

        string row = string.Create(CultureInfo.InvariantCulture,
            $"{tsUs},{op},{latencyUs},{(success ? 1 : 0)},{safeErr}");

        _channel.Writer.TryWrite(row);
    }

    private async Task DrainAsync()
    {
        var reader = _channel.Reader;
        int sinceFlush = 0;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var row))
            {
                await _writer.WriteLineAsync(row).ConfigureAwait(false);
                if (++sinceFlush >= 5000)
                {
                    await _writer.FlushAsync().ConfigureAwait(false);
                    sinceFlush = 0;
                }
            }
        }

        await _writer.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _channel.Writer.TryComplete();
        await _drainTask.ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
