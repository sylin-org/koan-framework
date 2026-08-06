namespace Koan.Data.Abstractions;

public sealed class RecordValueConversionException : InvalidCastException
{
    public RecordValueConversionException(DataField field, Type targetType, string correction, Exception? inner = null)
        : base(
            $"Record field '{field.Name}' at ordinal {field.Ordinal} (provider type " +
            $"'{field.ProviderTypeName ?? field.ClrType?.FullName ?? "unknown"}') cannot convert to " +
            $"'{targetType.FullName}'. {correction}",
            inner)
    {
        Field = field;
        TargetType = targetType;
        Correction = correction;
    }

    public DataField Field { get; }
    public Type TargetType { get; }
    public string Correction { get; }
}
