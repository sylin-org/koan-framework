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
        IEnumerable<string> unprovedClaims,
        IEnumerable<string> refusedRequirements)
    {
        Mapping = mapping;
        Table = table;
        Indexes = Array.AsReadOnly(indexes.ToArray());
        UnprovedClaims = Array.AsReadOnly(unprovedClaims.ToArray());
        RefusedRequirements = Array.AsReadOnly(refusedRequirements.ToArray());
    }

    public MappingPlan Mapping { get; }
    public RelationalTableDefinition Table { get; }
    public IReadOnlyList<RelationalIndexDefinition> Indexes { get; }

    /// <summary>Mapped intent this store cannot realize, named so it is degraded rather than silently dropped.</summary>
    public IReadOnlyList<string> UnprovedClaims { get; }

    /// <summary>
    /// Guarantees the application declared it depends on and this store cannot give it.
    ///
    /// <para>Separate from <see cref="UnprovedClaims"/> because the difference is the application's own
    /// statement, not the store's: the same shortfall is a degraded report until someone says they cannot work
    /// around it, and a refusal afterwards.</para>
    /// </summary>
    public IReadOnlyList<string> RefusedRequirements { get; }
}
