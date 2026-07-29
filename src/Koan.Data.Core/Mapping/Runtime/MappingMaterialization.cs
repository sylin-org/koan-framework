using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>A hydrated aggregate plus evidence of the bindings used.</summary>
public sealed record MappingMaterialization(object Entity, MappingReceipt Receipt);
