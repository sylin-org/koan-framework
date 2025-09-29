namespace Koan.Canon.Model;

public readonly record struct SourceId(string Value)
{ public override string ToString() => Value; }
public readonly record struct ReferenceId(string Value)
{ public override string ToString() => Value; }
public readonly record struct AggregationKey(string Value)
{ public override string ToString() => Value; }
public readonly record struct ProjectionVersion(ulong Value)
{ public override string ToString() => Value.ToString(); }

