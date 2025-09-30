using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.Postgres;

internal sealed class PgDialect : ILinqSqlDialect
{
    public string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    public string EscapeLike(string fragment)
        => fragment.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    public string Parameter(int index) => "@p" + index;
}
