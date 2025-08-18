using System;

namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Marks a property to never be persisted (not serialized nor indexed) by storage adapters.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreStorageAttribute : Attribute
{
}
