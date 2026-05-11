using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tagging;

/// <summary>
/// Domain-level tag registry entry. Manages the roster of canonical tag identities themselves,
/// distinct from the per-entity tag storage that <see cref="TagSet"/> provides.
/// </summary>
/// <remarks>
/// <para>
/// <b>Synonym, not hierarchy.</b> <see cref="ParentOf"/> is a synonym graph, not a taxonomic
/// hierarchy. <c>tag.Id = "ffxiv"</c> with <c>ParentOf = ["ff14", "final-fantasy-xiv"]</c>
/// means: whenever a user writes <c>"ff14"</c> or <c>"final-fantasy-xiv"</c>, normalise to
/// <c>"ffxiv"</c> at write time. There is no inheritance, no parent-walking, no
/// expansion at query time — just rename-on-write.
/// </para>
/// <para>
/// <b>Canonicalisation flow.</b> Before a tag string is set on an entity's
/// <see cref="TagSet"/>, callers can resolve it through the Tag registry:
/// <list type="number">
///   <item><description>Query: <c>Tag.Where(t =&gt; t.ParentOf.Contains(input))</c></description></item>
///   <item><description>If a match exists, use its <c>Id</c> as the canonical form.</description></item>
///   <item><description>If no match, the input is its own canonical form (and may itself be a Tag entity, or not).</description></item>
/// </list>
/// The convention is: not every tag string needs a Tag entity — only those where
/// canonicalisation matters (renames, deprecations, alias collapsing).
/// </para>
/// <para>
/// <b>Scope is open.</b> Consuming projects may attach scope or type metadata via additional
/// fields if they need it; this base entity stays small and reusable.
/// </para>
/// </remarks>
public class Tag : Entity<Tag>
{
    /// <summary>
    /// Canonical tag identifier — the form that gets stored on entities' <see cref="TagSet"/>s.
    /// Conventionally lowercase kebab-case (<c>"ffxiv"</c>, <c>"depth-of-field"</c>).
    /// </summary>
    [Index(Unique = true)]
    public new required string Id { get; set; } = default!;

    /// <summary>Human-readable display name (e.g. <c>"Final Fantasy XIV"</c>).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional description for the admin surface.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Synonym list. Each entry is an alias that, when encountered as user input, should be
    /// normalised to this Tag's <see cref="Id"/>. The relationship reads as
    /// "<see cref="Id"/> is the canonical form; these strings are synonyms of it."
    /// </summary>
    public List<string> ParentOf { get; set; } = new();

    /// <summary>
    /// Canonical taxonomic parent — the <see cref="Id"/> of a sibling Tag that contains this one.
    /// Distinct from <see cref="ParentOf"/> (synonym graph). Example:
    /// <c>Tag { Id="midlander", Parent="hyur" }</c>; <c>Tag { Id="hyur", Parent="race" }</c>.
    /// Consumers walk the chain at projection time to grow leaf tags into their ancestor set
    /// (e.g. catalog query "all Hyur presets" matches both <c>hyur</c> and any descendants).
    /// </summary>
    /// <remarks>
    /// String-Id self-reference, not a strongly-typed link — Mongo doesn't enforce referential
    /// integrity. Importers are expected to write parent rows before children. The walk is
    /// depth-capped at the call site to short-circuit pathological cycles. See ADR-0018.
    /// </remarks>
    [Index]
    public string? Parent { get; set; }

    /// <summary>
    /// When <see langword="true"/>, this Tag exists for grouping or hierarchy purposes only and
    /// should be filtered out of user-facing tag projections (e.g. <c>PackageSummary.PublicTags</c>).
    /// The walk still traverses through it; only the final rendered list excludes it. Useful for
    /// synthetic "category root" Tags (<c>race</c>, <c>tribe</c>, <c>patch</c>) whose only role
    /// is to be a uniform <see cref="Parent"/> pointer for their children.
    /// </summary>
    public bool NoRender { get; set; }
}
