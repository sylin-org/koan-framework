namespace Koan.Data.Abstractions;

public sealed record RecordSetExecution(
    RecordSetLimits EffectiveLimits,
    RecordSetByteAccounting ByteAccounting,
    long AccountedBytes,
    TimeSpan Elapsed);
