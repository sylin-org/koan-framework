namespace Koan.Data.Abstractions;

public sealed class RecordProjectionException : InvalidOperationException
{
    public RecordProjectionException(Type targetType, string correction)
        : base($"RecordSet cannot project to '{targetType.FullName}'. {correction}")
    {
        TargetType = targetType;
        Correction = correction;
    }

    public Type TargetType { get; }
    public string Correction { get; }
}
