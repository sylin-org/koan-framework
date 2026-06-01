using System.Text;
using Dapper;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Connector.PGVector;

/// <summary>The native PGVector filter: a parameterized WHERE fragment (no "WHERE" keyword) + its bound parameters.</summary>
internal sealed record PGVectorWhere(string? Sql, DynamicParameters Parameters);

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a parameterized SQL predicate over the JSONB
/// <c>metadata</c> column (AI-0036 §10 / DATA-0097 P1). PGVector has the richest filter substrate of
/// any vector adapter (full SQL over JSONB), so it is the reference: it declares the complete operator
/// set and emits the <b>null-inclusive</b> forms for <c>Ne/Nin/HasNone</c> so its result-set matches
/// the convergence oracle's locked null semantics. It is fail-loud — the coordinator has already
/// validated the tree is fully pushable, so a node outside capabilities here is a backstop throw.
/// The reader lowers <c>$between</c> → <c>AllOf(Gte,Lte)</c> and wildcards → StartsWith/EndsWith/Contains,
/// so this translator carries no Like/Between arms.
/// </summary>
internal sealed class PGVectorFilterTranslator : IVectorFilterTranslator<PGVectorWhere>
{
    public static readonly VectorFilterCapabilities Caps = VectorFilterCapabilities.Of(
        nestedPaths: true, ignoreCase: true,
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Exists,
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size);

    public VectorFilterCapabilities Capabilities => Caps;

    public PGVectorWhere Translate(Filter filter)
    {
        var parameters = new DynamicParameters();
        var sb = new StringBuilder();
        var counter = 0;
        Visit(filter, sb, parameters, ref counter);
        return new PGVectorWhere(sb.Length == 0 ? null : sb.ToString(), parameters);
    }

    /// <summary>Static convenience used by the repo: build the WHERE into an existing parameter bag.</summary>
    public static string? Translate(Filter? filter, DynamicParameters parameters)
    {
        if (filter is null) return null;
        var sb = new StringBuilder();
        var counter = 0;
        Visit(filter, sb, parameters, ref counter);
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void Visit(Filter filter, StringBuilder sb, DynamicParameters p, ref int c)
    {
        switch (filter)
        {
            case AllOf all: Compose(all.Operands, "AND", sb, p, ref c); break;
            case AnyOf any: Compose(any.Operands, "OR", sb, p, ref c); break;
            case Not not: sb.Append("NOT ("); Visit(not.Operand, sb, p, ref c); sb.Append(')'); break;
            case FieldFilter ff: sb.Append(Leaf(ff, p, ref c)); break;
            default:
                throw new NotSupportedException($"PGVector cannot translate filter node '{filter.GetType().Name}'.");
        }
    }

    private static void Compose(IReadOnlyList<Filter> operands, string op, StringBuilder sb, DynamicParameters p, ref int c)
    {
        if (operands.Count == 0) { sb.Append(op == "AND" ? "TRUE" : "FALSE"); return; }
        sb.Append('(');
        for (var i = 0; i < operands.Count; i++)
        {
            if (i > 0) sb.Append(' ').Append(op).Append(' ');
            Visit(operands[i], sb, p, ref c);
        }
        sb.Append(')');
    }

    private static string Leaf(FieldFilter f, DynamicParameters p, ref int c)
    {
        var segs = f.Field.Segments;
        var txt = TextAccessor(segs);    // metadata->>'k' / metadata#>>'{a,b}'  (scalar text)
        var json = JsonAccessor(segs);   // metadata->'k'  / metadata#>'{a,b}'   (jsonb, for arrays)
        var ic = f.IgnoreCase;

        switch (f.Operator)
        {
            case FilterOperator.Eq:
                return ic ? $"lower({txt}) = lower(@{Bind(p, ref c, Scalar(f))})"
                          : $"{txt} = @{Bind(p, ref c, Scalar(f))}";
            case FilterOperator.Ne:
                return ic ? $"({txt} IS NULL OR lower({txt}) <> lower(@{Bind(p, ref c, Scalar(f))}))"
                          : $"({txt} IS NULL OR {txt} <> @{Bind(p, ref c, Scalar(f))})";
            case FilterOperator.Gt: return $"({txt})::numeric > @{Bind(p, ref c, Num(f))}";
            case FilterOperator.Gte: return $"({txt})::numeric >= @{Bind(p, ref c, Num(f))}";
            case FilterOperator.Lt: return $"({txt})::numeric < @{Bind(p, ref c, Num(f))}";
            case FilterOperator.Lte: return $"({txt})::numeric <= @{Bind(p, ref c, Num(f))}";
            case FilterOperator.In:
                return $"{txt} = ANY(@{Bind(p, ref c, SetStrings(f))})";
            case FilterOperator.Nin:
                return $"({txt} IS NULL OR NOT ({txt} = ANY(@{Bind(p, ref c, SetStrings(f))})))";
            case FilterOperator.StartsWith: return Like(txt, $"{Esc(ScalarStr(f))}%", ic, p, ref c);
            case FilterOperator.EndsWith: return Like(txt, $"%{Esc(ScalarStr(f))}", ic, p, ref c);
            case FilterOperator.Contains: return Like(txt, $"%{Esc(ScalarStr(f))}%", ic, p, ref c);
            case FilterOperator.Exists:
                return (Scalar(f) is bool b && !b) ? $"{txt} IS NULL" : $"{txt} IS NOT NULL";
            case FilterOperator.Has:
                return $"jsonb_exists({json}, @{Bind(p, ref c, ScalarStr(f))})";
            case FilterOperator.HasAny:
                return $"jsonb_exists_any({json}, @{Bind(p, ref c, SetStrings(f))})";
            case FilterOperator.HasAll:
                return $"jsonb_exists_all({json}, @{Bind(p, ref c, SetStrings(f))})";
            case FilterOperator.HasNone:
                return $"({json} IS NULL OR NOT jsonb_exists_any({json}, @{Bind(p, ref c, SetStrings(f))}))";
            case FilterOperator.Size:
                return $"(jsonb_typeof({json}) = 'array' AND jsonb_array_length({json}) = @{Bind(p, ref c, Num(f))})";
            default:
                throw new NotSupportedException(
                    $"PGVector does not support vector filter operator '{f.Operator}' on metadata field '{f.Field}'.");
        }
    }

    private static string Like(string accessor, string pattern, bool ic, DynamicParameters p, ref int c)
        => $"{accessor} {(ic ? "ILIKE" : "LIKE")} @{Bind(p, ref c, pattern)}";

    private static string Bind(DynamicParameters p, ref int c, object? value)
    {
        var name = $"f{c++}";
        p.Add(name, value);
        return name;
    }

    // --- value extraction from the unified FilterValue ---

    private static object? Scalar(FieldFilter f) => f.Value switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static string ScalarStr(FieldFilter f) => Scalar(f)?.ToString() ?? "";

    // numeric comparisons bind the raw CLR number; ::numeric cast is on the accessor side.
    private static object? Num(FieldFilter f) => Scalar(f);

    private static string[] SetStrings(FieldFilter f) => f.Value switch
    {
        FilterValue.Set st => st.Values.Select(v => v?.ToString() ?? "").ToArray(),
        FilterValue.Scalar s => new[] { s.Value?.ToString() ?? "" },
        _ => Array.Empty<string>()
    };

    // --- JSONB accessors + identifier safety ---

    private static string TextAccessor(IReadOnlyList<string> segs)
        => segs.Count == 1
            ? $"metadata->>'{Sanitize(segs[0])}'"
            : $"metadata#>>'{{{string.Join(",", segs.Select(Sanitize))}}}'";

    private static string JsonAccessor(IReadOnlyList<string> segs)
        => segs.Count == 1
            ? $"metadata->'{Sanitize(segs[0])}'"
            : $"metadata#>'{{{string.Join(",", segs.Select(Sanitize))}}}'";

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // metadata keys are developer-controlled identifiers interpolated into the JSONB path literal
    // (values are always parameterized); reject quotes/braces/backslashes defensively.
    private static string Sanitize(string segment)
    {
        if (segment.IndexOfAny(new[] { '\'', '"', '\\', '{', '}' }) >= 0)
            throw new NotSupportedException($"Invalid metadata field name '{segment}'.");
        return segment;
    }
}
