using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Relational;

/// <summary>Rejects a relational command or schema plan whose physical facts diverge from its mapping receipt.</summary>
public static class RelationalPlanGuard
{
    public static void Validate(MappingPlan mapping, RelationalCommandPlan command)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(command);
        if (!string.Equals(command.Receipt.PlanId, mapping.Id, StringComparison.Ordinal))
            throw Error(mapping, command.Receipt.PlanId, "The relational command belongs to a different mapping plan.");
        var receipt = command.Receipt.BindingIds.ToHashSet(StringComparer.Ordinal);
        foreach (var binding in command.Values.Select(static value => value.Binding)
                     .Concat(command.Identity.Select(static value => value.Binding))
                     .Concat(command.Conditions.Select(static value => value.Binding))
                     .Concat(command.Reads)
                     .Concat(command.Filters)
                     .Concat(command.Orders))
        {
            var expected = mapping.Bindings.SingleOrDefault(candidate => candidate.Id == binding.BindingId)
                ?? throw Error(mapping, binding.BindingId, "The command references an unknown binding.");
            var expectedIdentity = mapping.Identity.Parts.Any(part => part.Id == expected.Id);
            var expectedEncoding = expected.Descriptor.Codec?.Id ?? $"clr:{expected.PhysicalType.AssemblyQualifiedName}";
            if (!expected.LogicalPath.Equals(binding.LogicalPath) ||
                !expected.PhysicalPath.Equals(binding.PhysicalPath) ||
                expected.Shape != binding.Shape ||
                expected.PhysicalType != binding.PhysicalType ||
                !string.Equals(expectedEncoding, binding.EncodingId, StringComparison.Ordinal) ||
                expectedIdentity != binding.IsIdentity)
                throw Error(mapping, binding.BindingId, "The command changed a compiled logical path, physical path, shape, type, encoding, or identity role.");
            if (!receipt.Contains(binding.BindingId))
                throw Error(mapping, binding.BindingId, "The command receipt omits a physical binding used by execution.");
        }
    }

    public static void Validate(MappingPlan mapping, RelationalSchemaPlan schema)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(schema);
        if (!ReferenceEquals(mapping, schema.Mapping))
            throw Error(mapping, schema.Mapping.Id, "The relational schema belongs to a different mapping plan instance.");
        foreach (var index in schema.Indexes)
        {
            var expected = mapping.Indexes.SingleOrDefault(candidate => string.Equals(candidate.Name, index.Name, StringComparison.Ordinal))
                ?? throw Error(mapping, index.Name, "The schema references an unknown mapped index.");
            var paths = expected.Bindings.Select(static binding => binding.PhysicalPath).ToArray();
            var encodings = expected.Bindings
                .Select(binding => binding.Descriptor.Codec?.Id ?? $"clr:{binding.PhysicalType.AssemblyQualifiedName}")
                .ToArray();
            if (!paths.SequenceEqual(index.Parts) || !encodings.SequenceEqual(index.EncodingIds, StringComparer.Ordinal) ||
                expected.Unique != index.Unique || expected.Primary != index.Primary || expected.Ttl != index.Ttl)
                throw Error(mapping, index.Name, "The schema changed a compiled index path, encoding, uniqueness, primary, or TTL decision.");
        }
    }

    private static MappingValueException Error(MappingPlan mapping, string binding, string correction) =>
        new(mapping.Id, binding, correction);
}
