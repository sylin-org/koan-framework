using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.Postgres;

internal sealed class PgDialect : ILinqSqlDialect
{
    public string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    public string EscapeLike(string fragment)
        => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public string Parameter(int index) => "@p" + index;

    // List<string> is stored as a jsonb array inside the Json column. jsonb_array_elements_text iterates it;
    // the column expression yields a jsonb value (cast from the json_extract path) so we test membership.
    public string JsonArrayContains(string columnSql, string parameter)
        => $"EXISTS (SELECT 1 FROM jsonb_array_elements_text(({columnSql})::jsonb) AS _e(value) WHERE _e.value = {parameter})";

    public string JsonArrayLength(string columnSql)
        => $"jsonb_array_length(({columnSql})::jsonb)";
}
