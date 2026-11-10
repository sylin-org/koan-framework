namespace Koan.Web.Hooks;

/// <summary>
/// Which operation of an entity endpoint a request serves (WEB-0089): what its hooks are told as <see cref="HookContext{TEntity}.Operation"/>,
/// and what a refused form's page is asked for. Whether a row stood before a write is <see cref="HookContext{TEntity}.Prior"/>'s to say.
/// </summary>
public enum EntityOperationKind
{
    /// <summary>A list of rows: the collection, or a query of it.</summary>
    Collection,

    /// <summary>The defaults of a row not yet written.</summary>
    New,

    /// <summary>One row, read by its address.</summary>
    Read,

    /// <summary>A write of a row by its members, one row or many: a declared create, which only creates, or an upsert.</summary>
    Create,

    /// <summary>A change of one row, which keeps what it leaves out.</summary>
    Change,

    /// <summary>A delete, of one row or many.</summary>
    Delete,
}
