using System.Text.Json.Serialization;
using Koan.Tagging.Json;

namespace Koan.Tagging;

/// <summary>
/// Model-facet tag container. Lives on entities as a property (e.g. <c>Package.Tags</c>) and
/// carries tags in two visibility scopes (<see cref="Public"/> / <see cref="Private"/>) each
/// further organised into open-ended named categories.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public vs. Private</b> is a serialisation boundary, not just a naming convention.
/// Public tags are intended to cross the surface — they appear on public REST APIs, in
/// catalog public-dataset exports, and on personal-library shares. Private tags stay inside
/// the platform: visible to curators and the owning entity's admin surfaces, never emitted
/// to anonymous consumers. The <see cref="PublicTags"/> property is the flat projection
/// public surfaces consume.
/// </para>
/// <para>
/// <b>Categories are open-ended.</b> Common conventions (per consuming-project documentation):
/// <list type="bullet">
///   <item><description><c>Public["game"]</c> — game tag matching the canonical default from the consuming project's game registry.</description></item>
///   <item><description><c>Public["source"]</c> — upstream host / platform provenance.</description></item>
///   <item><description><c>Public["technique"]</c> — for content like ReShade presets and shader packs, the effect / shader names used (or provided).</description></item>
///   <item><description><c>Public["aesthetic"]</c> — visual register tags.</description></item>
///   <item><description><c>Private["moderation"]</c> — curator-only flags.</description></item>
///   <item><description><c>Private["audit"]</c> — internal change tracking.</description></item>
/// </list>
/// New categories don't require code changes; the dictionary is open.
/// </para>
/// <para>
/// Usage:
/// <code>
/// var t = new TagSet();
/// t.Public["game"].Set(["ffxiv", "expedition-33"]);
/// t.Public["technique"].Set("dof").Set("clarity");
/// t.Private["moderation"].Set("review-pending");
///
/// t.Has("ffxiv");                            // true (defaults to Public scope)
/// t.Has("review-pending");                   // false (Public default)
/// t.Has("review-pending", TagSet.EScope.Private); // true
/// t.Find("ffxiv");                           // TagLocation(Public, "game")
/// t.PublicTags;                              // ["ffxiv", "expedition-33", "dof", "clarity"]
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(TagSetJsonConverter))]
public sealed class TagSet
{
    /// <summary>Scope a <see cref="Has(string, EScope)"/> query against.</summary>
    public enum EScope
    {
        /// <summary>Tags intended to cross the public surface. Default for query operations.</summary>
        Public,
        /// <summary>Tags scoped to the platform's curator / admin surfaces only.</summary>
        Private,
        /// <summary>Both public and private tags.</summary>
        All,
    }

    /// <summary>The public-visible side. Crosses the surface boundary when the entity is rendered.</summary>
    public TagScope Public { get; } = new();

    /// <summary>The private side. Stays inside the platform; never emitted on anonymous surfaces.</summary>
    public TagScope Private { get; } = new();

    /// <summary>
    /// Flat, de-duplicated list of every <see cref="Public"/> tag across all its categories.
    /// This is what public APIs and the public-dataset export serialise as the entity's
    /// <c>Tags</c> property. Computed on access (cheap; always consistent).
    /// </summary>
    public IReadOnlyList<string> PublicTags => Public.Flat;

    /// <summary>
    /// Flat, de-duplicated list of every <see cref="Private"/> tag across all its categories.
    /// Visible to curators / admin surfaces only.
    /// </summary>
    public IReadOnlyList<string> PrivateTags => Private.Flat;

    /// <summary>
    /// True when the tag is present in the requested scope.
    /// <see cref="EScope.Public"/> is the default; the common case for catalog / discovery queries.
    /// </summary>
    public bool Has(string tag, EScope scope = EScope.Public)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return scope switch
        {
            EScope.Public => Public.Contains(tag),
            EScope.Private => Private.Contains(tag),
            EScope.All => Public.Contains(tag) || Private.Contains(tag),
            _ => false,
        };
    }

    /// <summary>
    /// Where is this tag? Returns the (scope, category) location if found, or <see langword="null"/>.
    /// Searches public categories first, then private.
    /// </summary>
    public TagLocation? Find(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var pubCat = Public.Locate(tag);
        if (pubCat is not null) return new TagLocation(EScope.Public, pubCat);
        var privCat = Private.Locate(tag);
        if (privCat is not null) return new TagLocation(EScope.Private, privCat);
        return null;
    }

    /// <summary>True when both scopes are empty.</summary>
    public bool IsEmpty => Public.IsEmpty && Private.IsEmpty;
}
