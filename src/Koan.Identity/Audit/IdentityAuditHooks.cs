using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Identity.Audit;

/// <summary>
/// SEC-0007 Layer 1 — <b>audit-by-construction</b>. Hooks the entity lifecycle seam so every identity/access
/// mutation emits an append-only <see cref="AuditEvent"/> (before → after) with no "remember to log it". Registered
/// into each host composition over the identity-domain entities; <see cref="AuditEvent"/> itself is deliberately
/// NOT hooked, so emitting an audit row never recurses. Tamper-evidence + guaranteed delivery are P3 — here the
/// emission is best-effort relative to the mutation it records (a failed audit write never rolls back the operation).
/// </summary>
internal static class IdentityAuditHooks
{
    public static void Register()
    {
        Hook<Identity>("Identity", e => e.Id);
        Hook<IdentityEmail>("IdentityEmail", e => e.IdentityId);
        Hook<ExternalIdentityLink>("ExternalIdentityLink", e => e.IdentityId);
        Hook<Group>("Group", e => e.Id);
        Hook<Session>("Session", e => e.IdentityId);
        Hook<ApiToken>("ApiToken", e => e.IdentityId);
        Hook<IdentityRole>("IdentityRole", e => e.IdentityId);
        Hook<Impersonation.ImpersonationGrant>("ImpersonationGrant", e => e.Target);
    }

    private const string BeforeKey = "__koan_identity_audit_before";

    private static void Hook<TEntity>(string entityName, Func<TEntity, string> subjectOf)
        where TEntity : Entity<TEntity>
    {
        var verb = entityName.ToLowerInvariant();

        // Before/After share one context whose Prior value is the stable pre-write snapshot.
        Entity<TEntity>.Lifecycle.BeforeUpsert(ctx =>
        {
            ctx.Items[BeforeKey] = ctx.Prior;
            return ctx.Proceed();
        });

        Entity<TEntity>.Lifecycle.AfterUpsert(async ctx =>
        {
            var before = ctx.Items.TryGetValue(BeforeKey, out var b) ? b as TEntity : null;
            await EmitAsync(before is null ? $"{verb}.created" : $"{verb}.updated",
                entityName, ctx.Current.Id, subjectOf(ctx.Current), before, ctx.Current, ctx.CancellationToken).ConfigureAwait(false);
        });

        Entity<TEntity>.Lifecycle.AfterRemove(async ctx =>
        {
            var before = ctx.Prior;
            await EmitAsync($"{verb}.deleted",
                entityName, ctx.Current.Id, subjectOf(ctx.Current), before ?? ctx.Current, null, ctx.CancellationToken).ConfigureAwait(false);
        });
    }

    private static async Task EmitAsync(
        string action, string entityName, string id, string subject, object? before, object? after, CancellationToken ct)
    {
        try
        {
            var e = new AuditEvent
            {
                Actor = AppHost.Current?.GetService<IIdentityActorAccessor>()?.CurrentActorSubject,
                Subject = subject,
                Action = action,
                Target = $"{entityName}/{id}",
                Before = Snapshot(before),
                After = Snapshot(after),
            };

            // Tamper-evidence (Layer 3, opt-in): chain-stamp + write atomically (head advances only on success).
            if (AppHost.Current?.GetService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>()?.Value?.HashChainAudit == true
                && AppHost.Current?.GetService<AuditChain>() is { } chain)
                await chain.AppendAsync(e, ev => ev.Save(ct), ct).ConfigureAwait(false);
            else
                await e.Save(ct).ConfigureAwait(false);
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
