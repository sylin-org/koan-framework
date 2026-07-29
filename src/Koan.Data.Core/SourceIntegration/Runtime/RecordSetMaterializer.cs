using System.Diagnostics;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.SourceIntegration.Runtime;

internal sealed class RecordSetMaterializer
{
    public async Task<RecordSet> Materialize(
        INeutralRecordReader reader,
        RecordSetLimits limits,
        string operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        limits.Validate();

        await using var ownedReader = reader;
        var started = Stopwatch.GetTimestamp();
        var fields = reader.Fields.ToArray();
        var shapeBytes = RecordSetAccounting.MeasureShape(fields);
        var accountedBytes = shapeBytes;
        var records = new List<DataRecord>(Math.Min(limits.MaxRecords, 256));

        if (shapeBytes > limits.MaxBytes)
            return Result(RecordSetCompletion.ByteLimit);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (Elapsed() >= limits.MaxDuration)
                return Result(RecordSetCompletion.DurationLimit);

            var record = await reader.Read(ct).ConfigureAwait(false);
            if (record is null)
            {
                RejectAdditionalChannel();
                return Result(reader.Completion switch
                {
                    NeutralRecordReaderCompletion.Complete => RecordSetCompletion.Complete,
                    NeutralRecordReaderCompletion.ProviderLimit => RecordSetCompletion.ProviderLimit,
                    _ => throw new InvalidOperationException(
                        $"Neutral reader for '{operation}' returned an unknown completion value.")
                });
            }

            RejectAdditionalChannel();
            if (Elapsed() >= limits.MaxDuration)
                return Result(RecordSetCompletion.DurationLimit);
            if (records.Count >= limits.MaxRecords)
                return Result(RecordSetCompletion.RecordLimit);

            long recordBytes = 0;
            for (var ordinal = 0; ordinal < fields.Length; ordinal++)
            {
                if (!record.TryGetValue(ordinal, out var value)) continue;
                var valueBytes = RecordSetAccounting.MeasurePresentValue(value);
                if (valueBytes > limits.MaxValueBytes)
                    return Result(RecordSetCompletion.ValueLimit);
                recordBytes = checked(recordBytes + valueBytes);
            }

            if (recordBytes > limits.MaxBytes - accountedBytes)
                return Result(RecordSetCompletion.ByteLimit);

            records.Add(record);
            accountedBytes += recordBytes;
        }

        void RejectAdditionalChannel()
        {
            if (reader.HasAdditionalResultChannels)
                throw new AdditionalResultChannelsNotSupportedException(operation);
        }

        TimeSpan Elapsed() => Stopwatch.GetElapsedTime(started);

        RecordSet Result(RecordSetCompletion completion) => new(
            fields,
            records,
            completion,
            new RecordSetExecution(
                limits,
                RecordSetByteAccounting.MaterializedValueV1,
                accountedBytes,
                Elapsed()));
    }
}
