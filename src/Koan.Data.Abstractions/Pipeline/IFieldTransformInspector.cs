namespace Koan.Data.Abstractions.Pipeline;

/// <summary>Read-only host inspection used by Cache and diagnostics to honor stored-value transforms.</summary>
public interface IFieldTransformInspector
{
    bool HasTransformsFor(Type entityType);

    IReadOnlyList<string> ContributorIdsFor(Type entityType);
}
