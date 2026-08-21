namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// One table, as the schema owner asks for it: where it lives, what it holds, and which columns carry identity.
///
/// <para>It is the subject of every <see cref="IRelationalDdlExecutor"/> member, so an adapter is always told
/// which table before it is told what to do with it, and a schema and a name can no longer be handed over in
/// the wrong order.</para>
///
/// <para><see cref="Identity"/> is ordered because a composite primary key is an ordered thing: the store
/// builds its index in the order it is given, and the mapping's declaration order is the only order that
/// carries intent.</para>
/// </summary>
public sealed record RelationalTableDefinition(
    string Schema,
    string Name,
    IReadOnlyList<RelationalColumnDefinition> Columns,
    IReadOnlyList<string> Identity)
{
    /// <summary>How a refusal or a mismatch names this table. Quoting is grammar, so it is not applied here.</summary>
    public override string ToString() => $"{Schema}/{Name}";
}
