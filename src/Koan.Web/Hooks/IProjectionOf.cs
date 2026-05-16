namespace Koan.Web.Hooks;

/// <summary>
/// Static-abstract contract declaring that <typeparamref name="TSelf"/> is a projection of
/// <typeparamref name="TEntity"/>. Used by <see cref="Controllers.EntitySummaryController{TEntity, TSummary}"/>
/// to map list responses through the implementer's <see cref="From"/> factory without per-controller
/// boilerplate.
/// </summary>
/// <remarks>
/// <para>
/// Typical usage on a record-style summary DTO:
/// <code>
/// public sealed record PackageSummary(string Id, string? Name, string? Author)
///     : IProjectionOf&lt;Package, PackageSummary&gt;
/// {
///     public static PackageSummary From(Package p) =&gt; new(p.Id, p.Name, p.Author);
/// }
/// </code>
/// </para>
/// <para>
/// The static-abstract member requires C# 11 / .NET 7+. Implementers can also expose an
/// <c>implicit operator</c> for ad-hoc casting; the controller pipeline only invokes
/// <see cref="From"/>.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The full domain entity being projected.</typeparam>
/// <typeparam name="TSelf">The projecting type itself (CRTP-style self-reference).</typeparam>
public interface IProjectionOf<in TEntity, out TSelf>
    where TSelf : IProjectionOf<TEntity, TSelf>
{
    /// <summary>Project a single <typeparamref name="TEntity"/> into <typeparamref name="TSelf"/>.</summary>
    static abstract TSelf From(TEntity entity);
}
