using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Data.Core.Transactions;

namespace Koan.Identity.Roles;

/// <summary>
/// The role model's reads of what grows. Each is bounded: by its page, or by the one thing it evaluates. A store may
/// compare keys by its own collation — one that ignores case matches <c>Doc</c> when asked for <c>doc</c> — so every
/// read that feeds authorization keeps only the rows whose keys equal the asked ones ordinally, checked here after the
/// store answers.
/// </summary>
internal static class RoleReads
{
    private const char CursorSeparator = ':';

    /// <summary>The hold the calling flow is in, while it is open.</summary>
    public static ITransactionCoordinator? OpenHold =>
        EntityContext.CurrentTransaction is { IsHeld: true, IsCompleted: false } hold ? hold : null;

    /// <summary>The role keyed exactly <paramref name="roleKey"/>, or null: one read of its composed id.</summary>
    public static async Task<Role?> ExactRole(string roleKey, CancellationToken ct)
        => await Role.Get(Role.KeyFor(roleKey), ct).ConfigureAwait(false) is { } role && StringComparer.Ordinal.Equals(role.Key, roleKey) ? role : null;

    /// <summary>The roles keyed exactly <paramref name="roleKeys"/> that exist, in key order: one read of their composed ids.</summary>
    public static async Task<IReadOnlyList<Role>> ExactRoles(IReadOnlyCollection<string> roleKeys, CancellationToken ct)
    {
        if (roleKeys.Count == 0) return [];
        var asked = roleKeys.ToHashSet(StringComparer.Ordinal);
        var roles = await Role.Get(asked.Select(Role.KeyFor), ct).ConfigureAwait(false);
        return roles.OfType<Role>().Where(role => asked.Contains(role.Key)).DistinctBy(role => role.Key, StringComparer.Ordinal)
            .OrderBy(role => role.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>A page of <paramref name="personId"/>'s memberships in role order, and the cursor that continues it.</summary>
    public static Task<(IReadOnlyList<RoleMember> Rows, string? Next)> MembershipPage(string personId, string? after, int take, CancellationToken ct)
        => Page<RoleMember>(member => member.PersonId == personId, member => StringComparer.Ordinal.Equals(member.PersonId, personId),
            member => member.RoleKey, member => member.RoleKey, after, take, ct);

    /// <summary>
    /// <paramref name="personId"/>'s memberships, at most <paramref name="limit"/>, read a page of
    /// <paramref name="pageSize"/> at a time by person.
    /// </summary>
    public static async Task<IReadOnlyList<RoleMember>> MembershipsOf(string personId, int limit, int pageSize, CancellationToken ct)
    {
        var memberships = new List<RoleMember>();
        for (string? after = null; memberships.Count < limit;)
        {
            var (rows, next) = await MembershipPage(personId, after, Math.Min(pageSize, limit - memberships.Count), ct).ConfigureAwait(false);
            memberships.AddRange(rows);
            if ((after = next) is null) break;
        }

        return memberships;
    }

    /// <summary>
    /// The roles <paramref name="personId"/> holds, in key order: their memberships by person and the roles they name.
    /// A person holds at most <paramref name="max"/> roles; more fails closed, since a bag compiled from part of them
    /// would be wrong.
    /// </summary>
    public static async Task<IReadOnlyList<Role>> RolesOf(string personId, int max, int pageSize, CancellationToken ct)
    {
        var memberships = await MembershipsOf(personId, max + 1, pageSize, ct).ConfigureAwait(false);
        if (memberships.Count > max)
            throw new InvalidOperationException(
                $"Person '{personId}' holds more roles than the configured bound of {max} " +
                $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxRolesPerPerson)}); remove one of their roles or raise the bound.");
        return await ExactRoles(memberships.Select(member => member.RoleKey).ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// At most <paramref name="take"/> rows of <paramref name="scope"/> after the cursor <paramref name="after"/>, in
    /// <paramref name="key"/>'s order, and the cursor that continues them — null after the last.
    /// <para>The store reads the rows its own comparison puts in scope, ordered by the key and then by row id, which a
    /// paged read always breaks ties with; the page after a cursor is the rows whose key sorts after the cursor's key,
    /// or equals it with a later id. The cursor is the last row the store returned — its own key and id, never the
    /// asked scope — so a store that equates two keys continues past both. Of those rows the page keeps the ones
    /// <paramref name="exact"/> accepts, which compares ordinally; a page may therefore hold fewer than
    /// <paramref name="take"/> rows, or none, while a cursor still follows.</para>
    /// </summary>
    public static async Task<(IReadOnlyList<T> Rows, string? Next)> Page<T>(Expression<Func<T, bool>> scope, Func<T, bool> exact,
        Expression<Func<T, string>> key, Func<T, string> keyOf, string? after, int take, CancellationToken ct)
        where T : class, IEntity<string>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var field = FieldPath.Of(((MemberExpression)key.Body).Member.Name);
        var query = new QueryDefinition().WithSort<T>(sort => sort.OrderBy(key)).WithPagination(1, take + 1);
        if (ParseCursor(after) is var (afterKey, afterId))
        {
            query = query.Where(Filter.Any(
                Filter.On(field, FilterOperator.Gt, FilterValue.Of(afterKey)),
                Filter.All(
                    Filter.On(field, FilterOperator.Eq, FilterValue.Of(afterKey)),
                    Filter.On(FieldPath.Of(nameof(IEntity<string>.Id)), FilterOperator.Gt, FilterValue.Of(afterId)))));
        }

        var scanned = await Data<T, string>.Query(scope, query, ct).ConfigureAwait(false);
        return Keep(scanned, exact, keyOf, take);
    }

    /// <summary>
    /// Of the rows a store returned for a page of <paramref name="take"/> — one more when more follow — the ones
    /// <paramref name="exact"/> accepts, and the cursor after the last row the page covers.
    /// </summary>
    public static (IReadOnlyList<T> Rows, string? Next) Keep<T>(IReadOnlyList<T> scanned, Func<T, bool> exact, Func<T, string> keyOf, int take)
        where T : IEntity<string>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        if (scanned.Count <= take) return (scanned.Where(exact).ToArray(), null);
        var covered = scanned.Take(take).ToArray();
        return (covered.Where(exact).ToArray(), Cursor(keyOf(covered[^1]), covered[^1].Id));
    }

    /// <summary>The cursor after the row whose key is <paramref name="key"/> and whose id is <paramref name="id"/>.</summary>
    public static string Cursor(string key, string id) => $"{id}{CursorSeparator}{key}";

    /// <summary>The key and id a cursor names, or null for none; anything else is refused.</summary>
    public static (string Key, string Id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        var at = cursor.IndexOf(CursorSeparator, StringComparison.Ordinal);
        if (at < 1 || !RoleContract.TryRequire(cursor[..at], out var id) || !RoleContract.TryRequire(cursor[(at + 1)..], out var key))
            throw new ArgumentException($"'{cursor}' is not a cursor this listing handed back.", nameof(cursor));
        return (key, id);
    }
}
