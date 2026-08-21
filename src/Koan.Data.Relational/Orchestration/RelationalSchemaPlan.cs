using Koan.Data.Relational.Orchestration;
using Koan.Data.Core;

namespace Koan.Data.Relational;

/// <summary>The complete relational shape derived from one compiled mapping plan.</summary>
public sealed class RelationalSchemaPlan
{
    internal RelationalSchemaPlan(
        MappingPlan mapping,
        RelationalTableDefinition table,
        IEnumerable<RelationalIndexDefinition> indexes,
        IEnumerable<string> unprovedClaims)
    {
        Mapping = mapping;
        Table = table;
        Indexes = Array.AsReadOnly(indexes.ToArray());
        UnprovedClaims = Array.AsReadOnly(unprovedClaims.ToArray());
    }

    public MappingPlan Mapping { get; }
    public RelationalTableDefinition Table { get; }
    public IReadOnlyList<RelationalIndexDefinition> Indexes { get; }

    /// <summary>Mapped intent this store cannot realize, named so it is degraded rather than silently dropped.</summary>
    public IReadOnlyList<string> UnprovedClaims { get; }
}
