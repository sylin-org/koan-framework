namespace Koan.Data.Relational.Schema;

public interface IRelationalDialect
{
    string QuoteIdent(string ident);
    string MapType(Type clr, bool isJson);
    string CreateTable(RelationalTable table);
    IEnumerable<string> CreateIndexes(RelationalTable table);
}