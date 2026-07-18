using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>
/// A declarative descriptor for a CLASSIFIED POCO PROPERTY — an existing backing property whose VALUE the
/// framework round-trips (transform on write, reverse on read). The structural inverse of
/// <see cref="ManagedFieldDescriptor"/> (which <i>injects</i> a non-POCO, one-way, ambient-sourced field): a
/// classified field has a backing <see cref="Property"/>, is round-trip, entity-sourced, and value-PROTECTED.
/// ARCH-0098 §1.
///
/// <para>This is data, not behaviour: it carries the declared <see cref="Category"/> and two Expression-compiled
/// accessors. Handling remains owned by an optional field-transform contributor, never the Entity fact.</para>
/// </summary>
/// <param name="Property">The backing POCO property whose value is protected.</param>
/// <param name="Category">The declared classification category fact.</param>
/// <param name="Getter">Reads the property value (boxed); compiled once per type.</param>
/// <param name="Setter">Writes the property value (boxed); compiled once per type.</param>
public sealed record ClassifiedFieldDescriptor(
    PropertyInfo Property,
    ClassificationCategory Category,
    Func<object, object?> Getter,
    Action<object, object?> Setter);
