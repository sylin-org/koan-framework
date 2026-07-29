namespace Koan.Data.Abstractions;

/// <summary>A typed pre-dispatch failure for an invalid aggregate mapping declaration.</summary>
public sealed class MappingCompilationException : InvalidOperationException
{
    public MappingCompilationException(string source, Type entityType, string correction)
        : base($"Mapping for '{entityType.FullName}' on source '{source}' is invalid. {correction}")
    {
        SourceName = source;
        EntityType = entityType;
        Correction = correction;
    }

    public string SourceName { get; }
    public Type EntityType { get; }
    public string Correction { get; }
}
