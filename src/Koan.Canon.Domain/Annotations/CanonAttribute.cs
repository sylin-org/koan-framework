using System;

namespace Koan.Canon.Domain.Annotations;

/// <summary>
/// Declares canonical model specific behaviours, such as auditing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CanonAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CanonAttribute"/> class.
    /// </summary>
    /// <param name="audit">True to enable audit logging for the canonical type.</param>
    public CanonAttribute(bool audit = false)
    {
        Audit = audit;
    }

    /// <summary>
    /// Gets a value indicating whether auditing is enabled for the canonical type.
    /// </summary>
    public bool Audit { get; }
}
