namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Marks a DateTimeOffset property for automatic timestamp management.
/// Default behavior: set once when value is default (creation stamp).
/// With OnSave = true: updated on every save operation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class TimestampAttribute : Attribute
{
    /// <summary>
    /// When true, the property is updated on every save (UpdatedAt semantics).
    /// When false (default), the property is set only when its value is default/zero (CreatedAt semantics).
    /// </summary>
    public bool OnSave { get; set; }
}
