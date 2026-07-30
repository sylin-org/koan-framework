using Koan.Data.Relational.Orchestration;
using Koan.Data.Core;

namespace Koan.Data.Relational;

/// <summary>The complete relational shape derived from one compiled mapping plan.</summary>
public sealed class RelationalSchemaPlan
{
    internal RelationalSchemaPlan(
        MappingPlan mapping,
        string schema,
        string table,
        IEnumerable<RelationalColumnDefinition> columns,
        IEnumerable<RelationalIndexDefinition> indexes,
        IEnumerable<string> unprovedClaims)
    {
        Mapping = mapping;
        Schema = schema;
        Table = table;
        Columns = Array.AsReadOnly(columns.ToArray());
        Indexes = Array.AsReadOnly(indexes.ToArray());
        UnprovedClaims = Array.AsReadOnly(unprovedClaims.ToArray());
    }

    public MappingPlan Mapping { get; }
    public string Schema { get; }
    public string Table { get; }
    public IReadOnlyList<RelationalColumnDefinition> Columns { get; }
    public IReadOnlyList<RelationalIndexDefinition> Indexes { get; }
    public IReadOnlyList<string> UnprovedClaims { get; }
}
