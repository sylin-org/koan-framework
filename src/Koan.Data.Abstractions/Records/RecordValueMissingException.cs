namespace Koan.Data.Abstractions;

public sealed class RecordValueMissingException : InvalidOperationException
{
    public RecordValueMissingException(DataField field)
        : base($"Record value '{field.Name}' at ordinal {field.Ordinal} is missing from this record.")
        => Field = field;

    public DataField Field { get; }
}
