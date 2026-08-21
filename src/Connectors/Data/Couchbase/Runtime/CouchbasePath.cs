namespace Koan.Data.Connector.Couchbase.Runtime;

/// <summary>
/// How Couchbase spells a path into a document.
///
/// <para>Non-generic on purpose: the same spelling has to serve a filter compiled for one entity and an index
/// built for a container, and a grammar that only exists inside a generic plan cannot be shared by both. An
/// index whose path disagrees with the filters it exists to serve is an index the query service will not use.</para>
/// </summary>
internal static class CouchbasePath
{
    internal static string Quote(IEnumerable<string> segments) =>
        string.Join('.', segments.Select(static segment => "`" + segment.Replace("`", "``", StringComparison.Ordinal) + "`"));

    internal static string Quote(string identifier) =>
        "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
}
