using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Orchestration;

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
            // A tenant-scoped unique index (DATA-0150) is its mapped declaration led by managed parts: the declaration
            // is checked as compiled, and the scope may only add leading parts that read inside the document.
            var declared = index.Unscoped ?? index;
            var expected = mapping.Indexes.SingleOrDefault(candidate => string.Equals(candidate.Name, declared.Name, StringComparison.Ordinal))
                ?? throw Error(mapping, declared.Name, "The schema references an unknown mapped index.");
            var parts = expected.Bindings.Select(static binding => new RelationalIndexPart(
                binding.PhysicalPath,
                binding.PhysicalType,
                binding.Descriptor.Codec?.Id ?? $"clr:{binding.PhysicalType.AssemblyQualifiedName}")).ToArray();
            if (!parts.SequenceEqual(declared.Parts) ||
                expected.Unique != declared.Unique || expected.Primary != declared.Primary || expected.Ttl != declared.Ttl)
                throw Error(mapping, index.Name, "The schema changed a compiled index path, type, encoding, uniqueness, primary, or TTL decision.");
            if (index.Unscoped is null) continue;
            var scope = index.Parts.Count - parts.Length;
            if (scope <= 0 || !index.Unique || index.Primary || index.Ttl != declared.Ttl ||
                !index.Parts.Skip(scope).SequenceEqual(parts) ||
                index.Parts.Take(scope).Any(static part => !part.Path.IsNested))
                throw Error(mapping, index.Name, "A scoped unique index must be its declaration led by managed parts inside the document.");
        }
    }

    private static MappingValueException Error(MappingPlan mapping, string binding, string correction) =>
        new(mapping.Id, binding, correction);
}
