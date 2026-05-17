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
    /// Requires the repository to implement IStringQueryRepository. Default false because most
    /// adapters only implement ILinqQueryRepository and silently degrade to "delete all" when
    /// given an unparseable string filter (caught during matrix validation).
    /// </summary>
    bool SupportsDeleteByQuery => false;

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

    /// <summary>POST /query with a JSON body filter (?q= equivalent).</summary>
    bool SupportsBodyQuery => true;

    /// <summary>
    /// GET / with ?filter=&lt;filter-json&gt; query-string filter. Same caveat as
    /// SupportsDeleteByQuery — requires IStringQueryRepository for a meaningful evaluation.
    /// </summary>
    bool SupportsQueryStringFilter => false;
}
