namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// A host-owned contributor that builds one immutable round-trip transform for an applicable Entity type.
/// </summary>
public interface IFieldTransformContributor
{
    /// <summary>Stable diagnostic identity.</summary>
    string Id { get; }

    /// <summary>Stable transform order; ties are resolved by <see cref="Id"/>.</summary>
    int Order => 0;

    /// <summary>Build the transform for <paramref name="entityType"/>, or return null when it does not apply.</summary>
    IFieldTransform? Build(Type entityType);
}
