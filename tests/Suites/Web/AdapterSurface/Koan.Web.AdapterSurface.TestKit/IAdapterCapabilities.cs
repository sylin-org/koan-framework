namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Capability declaration that gates per-spec execution. Adapters declare what they support;
/// specs that need a missing capability skip cleanly with a clear reason.
/// </summary>
/// <remarks>
/// Defaults assume a fully-featured document/relational adapter. KV / specialised adapters override
/// the relevant flags to false. The matrix's job is to verify what each adapter CLAIMS to support
/// actually works — not to force every adapter to support every operation.
/// </remarks>
public interface IAdapterCapabilities
{
    /// <summary>RFC 6902 JSON Patch semantics on PATCH /{id}.</summary>
    bool SupportsJsonPatch => true;

    /// <summary>RFC 7396 Merge Patch semantics on PATCH /{id}.</summary>
    bool SupportsMergePatch => true;

    /// <summary>Partial-JSON PATCH semantics (Koan's default fallback content type).</summary>
    bool SupportsPartialPatch => true;

    /// <summary>
    /// DELETE /?q=&lt;filter-json&gt;: server-evaluated filter delete.
    /// Default true: EntityEndpointService routes through JsonFilterBuilder → LINQ predicate,
    /// which works on any ILinqQueryRepository adapter. Adapters without ILinqQueryRepository
    /// or with restrictive query providers can opt out.
    /// </summary>
    bool SupportsDeleteByQuery => true;

    /// <summary>DELETE /bulk with body of ids.</summary>
    bool SupportsBulkDelete => true;

    /// <summary>DELETE /all (drop every row).</summary>
    bool SupportsDeleteAll => true;

    /// <summary>POST /bulk upsert-many.</summary>
    bool SupportsBulkUpsert => true;

    /// <summary>?set= / EntityContext partitions / entity sets.</summary>
    bool SupportsPartitions => true;

    /// <summary>Entity&lt;T&gt;.Copy()/Move()/Mirror() across partitions.</summary>
    bool SupportsCrossPartitionTransfer => true;

    /// <summary>POST /query with a JSON body filter (?filter= equivalent).</summary>
    bool SupportsBodyQuery => true;

    /// <summary>
    /// GET / with ?filter=&lt;filter-json&gt; query-string filter.
    /// Default true: routed through JsonFilterBuilder → LINQ predicate, works on any
    /// ILinqQueryRepository adapter.
    /// </summary>
    bool SupportsQueryStringFilter => true;

    /// <summary>
    /// The adapter runs provider-native queries <b>and confines them to one read-only statement</b> (WEB-0074), so a
    /// controller that declares <c>AllowRawQuery</c> can be exercised end to end. Default false; SQLite and PostgreSQL
    /// opt in. An adapter whose raw path is not confined must not claim it: the opt-in specs would execute stacked
    /// statements against it.
    /// </summary>
    bool SupportsRawQuery => false;
}
