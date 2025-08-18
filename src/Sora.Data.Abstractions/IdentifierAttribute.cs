using System;

namespace Sora.Data.Abstractions.Annotations;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class IdentifierAttribute : Attribute
{
}
