namespace Koan.Data.Abstractions;

public sealed record RecordSetLimits(
    int MaxRecords,
    long MaxBytes,
    long MaxValueBytes,
    TimeSpan MaxDuration)
{
    public void Validate()
    {
        if (MaxRecords <= 0) throw new ArgumentOutOfRangeException(nameof(MaxRecords));
        if (MaxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxBytes));
        if (MaxValueBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxValueBytes));
        if (MaxDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaxDuration));
    }
}
