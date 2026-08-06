using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Koan.Core.Json;

namespace Koan.Data.Core.Transfers;

/// <summary>
/// A delete-on-close, length-prefixed journal used when a transfer must defer work without retaining
/// the selected dataset in application memory. It is deliberately private to transfer execution.
/// </summary>
internal sealed class TransferJournal<T> : IAsyncDisposable
{
    private const int HeaderSize = sizeof(int);
    private readonly FileStream _stream;
    private bool _reading;

    public TransferJournal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-transfer-{Guid.CreateVersion7():n}.tmp");
        _stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 16 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose
        });
    }

    public async ValueTask Append(T value, CancellationToken ct)
    {
        if (_reading) throw new InvalidOperationException("A transfer journal cannot be appended after reading begins.");
        ct.ThrowIfCancellationRequested();

        var payload = Encoding.UTF8.GetBytes(value.ToJson());
        if (payload.Length > Infrastructure.Constants.Defaults.SourceMaxValueBytes)
            throw new InvalidOperationException(
                $"A transfer journal value exceeded Koan's {Infrastructure.Constants.Defaults.SourceMaxValueBytes}-byte value bound. " +
                "Narrow the transfer or use an application-owned durable workflow.");

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await _stream.WriteAsync(header, ct).ConfigureAwait(false);
        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<IReadOnlyList<T>> ReadBatches(
        int batchSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (!_reading)
        {
            _reading = true;
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            _stream.Position = 0;
        }
        else if (_stream.Position != 0)
        {
            throw new InvalidOperationException("A transfer journal can be enumerated once.");
        }

        var header = new byte[HeaderSize];
        var batch = new List<T>(batchSize);
        while (await ReadHeader(header, ct).ConfigureAwait(false))
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 0 || length > Infrastructure.Constants.Defaults.SourceMaxValueBytes)
                throw new InvalidDataException("The bounded transfer journal contains an invalid value length.");

            var payload = new byte[length];
            await _stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
            var value = Encoding.UTF8.GetString(payload).FromJson<T>();
            if (value is null) throw new InvalidDataException("The bounded transfer journal contains a null value.");
            batch.Add(value);
            if (batch.Count != batchSize) continue;
            yield return batch;
            batch = new List<T>(batchSize);
        }

        if (batch.Count != 0) yield return batch;
    }

    private async ValueTask<bool> ReadHeader(byte[] header, CancellationToken ct)
    {
        var offset = 0;
        while (offset != HeaderSize)
        {
            var read = await _stream.ReadAsync(header.AsMemory(offset, HeaderSize - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new EndOfStreamException("The bounded transfer journal ended inside a value header.");
            }
            offset += read;
        }
        return true;
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
