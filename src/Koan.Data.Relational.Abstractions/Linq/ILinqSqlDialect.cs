namespace Koan.Data.Relational.Linq;

/// <summary>Defines the minimal SQL grammar required by Koan's relational filter translator.</summary>
public interface ILinqSqlDialect
{
    string QuoteIdent(string ident);
    string EscapeLike(string fragment);
    string Parameter(int index);
    string JsonArrayContains(string columnSql, string parameter);
    string JsonArrayLength(string columnSql);
}
