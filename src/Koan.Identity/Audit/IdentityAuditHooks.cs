using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Identity.Audit;

/// <summary>
/// SEC-0007 Layer 1 — <b>audit-by-construction</b>. Hooks the entity lifecycle seam so every identity/access
/// mutation emits an append-only <see cref="AuditEvent"/> (before → after) with no "remember to log it". Registered
/// once per process (idempotent) over the identity-domain entities; <see cref="AuditEvent"/> itself is deliberately
/// NOT hooked, so emitting an audit row never recurses. Tamper-evidence + guaranteed delivery are P3 — here the
/// emission is best-effort relative to the mutation it records (a failed audit write never rolls back the operation).
/// </summary>
internal static class IdentityAuditHooks
{
    private static readonly object Gate = new();
    // Couples to the process-static EntityEventRegistry below: if a future test-isolation path ever calls
    // Entity<T>.Events.Reset(), it MUST also clear this flag, or auditing would silently stay off (fail-open).
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;
            Hook<Identity>("Identity", e => e.Id);
            Hook<IdentityEmail>("IdentityEmail", e => e.IdentityId);
            Hook<ExternalIdentityLink>("ExternalIdentityLink", e => e.IdentityId);
            Hook<Group>("Group", e => e.Id);
            Hook<Session>("Session", e => e.IdentityId);
            Hook<ApiToken>("ApiToken", e => e.IdentityId);
            _registered = true;
        }
    }

    private const string BeforeKey = "__koan_identity_audit_before";

    private static void Hook<TEntity>(string entityName, Func<TEntity, string> subjectOf)
        where TEntity : Entity<TEntity>
    {
        var verb = entityName.ToLowerInvariant();

        // Capture the pre-write image BEFORE persist (the Prior loader re-reads the store, which after persist would
        // return the new row — so create/update can only be distinguished here). Before/After share one context.
        Entity<TEntity>.Events.BeforeUpsert(async ctx =>
        {
            ctx.Items[BeforeKey] = await ctx.Prior.Get(ctx.CancellationToken).ConfigureAwait(false);
            return ctx.Proceed();
        });

        Entity<TEntity>.Events.AfterUpsert(async ctx =>
        {
            var before = ctx.Items.TryGetValue(BeforeKey, out var b) ? b as TEntity : null;
            await EmitAsync(before is null ? $"{verb}.created" : $"{verb}.updated",
                entityName, ctx.Current.Id, subjectOf(ctx.Current), before, ctx.Current, ctx.CancellationToken).ConfigureAwait(false);
        });

        Entity<TEntity>.Events.AfterRemove(async ctx =>
        {
            var before = await ctx.Prior.Get(ctx.CancellationToken).ConfigureAwait(false);
            await EmitAsync($"{verb}.deleted",
                entityName, ctx.Current.Id, subjectOf(ctx.Current), before ?? ctx.Current, null, ctx.CancellationToken).ConfigureAwait(false);
        });
    }

    private static async Task EmitAsync(
        string action, string entityName, string id, string subject, object? before, object? after, CancellationToken ct)
    {
        try
        {
            await new AuditEvent
            {
                Actor = AppHost.Current?.GetService<IIdentityActorAccessor>()?.CurrentActorSubject,
                Subject = subject,
                Action = action,
                Target = $"{entityName}/{id}",
                Before = Snapshot(before),
                After = Snapshot(after),
            }.Save(ct).ConfigureAwait(false);
        }
        catch
        {
            // Audit emission is best-effort relative to the already-committed mutation it records; never throw out
            // of the lifecycle hook (that would surface a post-commit audit failure as an operation failure).
        }
    }

    // Serialize the before/after image, redacting free-form provider PII (ExternalIdentityLink.ClaimsJson = raw
    // userinfo) so the audit channel does not fan that blob out (relevant once P3 streams audit to a SIEM).
    private static string? Snapshot(object? entity) => entity switch
    {
        null => null,
        ExternalIdentityLink link => JsonConvert.SerializeObject(new
        {
            link.Id,
            link.IdentityId,
            link.Provider,
            link.ProviderKeyHash,
            link.CreatedAt,
            ClaimsJson = link.ClaimsJson is null ? null : "[redacted]",
        }),
        _ => JsonConvert.SerializeObject(entity),
    };
}
