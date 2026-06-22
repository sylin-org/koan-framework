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
/// <para>This is DATA, not behaviour: it carries the declared FACTS (<see cref="Category"/>,
/// <see cref="Searchable"/>) and two Expression-compiled accessors (built once per type). The HANDLING — which
/// transform, the crypto — is resolved separately by policy + the <c>Koan.Classification</c> module, never on the
/// entity (the facts/handling separation, §2). The descriptor deliberately does NOT carry a transform-kind: kind
/// is policy-resolved handling, not an entity fact.</para>
/// </summary>
/// <param name="Property">The backing POCO property whose value is protected.</param>
/// <param name="Category">The declared classification category fact.</param>
/// <param name="Searchable">Whether blind-equality search is requested for this field (ARCH-0098 §0).</param>
/// <param name="Getter">Reads the property value (boxed); compiled once per type.</param>
/// <param name="Setter">Writes the property value (boxed); compiled once per type.</param>
public sealed record ClassifiedFieldDescriptor(
    PropertyInfo Property,
    ClassificationCategory Category,
    bool Searchable,
    Func<object, object?> Getter,
    Action<object, object?> Setter);
