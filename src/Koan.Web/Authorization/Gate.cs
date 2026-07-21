using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A/§D) — a small typed fluent builder for an <see cref="ActionGate"/>, for the gate overrides on
/// <see cref="EntityAccess{TEntity}"/>. Mirrors the <c>[Access]</c> vocabulary with refactor-safe, compile-checked
/// terms — <c>Gate.Is("admin").Or(Gate.Owner)</c> — and, unlike the single-term <c>[Access]</c> string,
/// <see cref="And"/> lights up the model's multi-term bags (<c>Gate.Is("member").And(Gate.HasScope("x"))</c>).
/// Implicitly converts to <see cref="ActionGate"/>.
/// </summary>
public sealed class GateBuilder
{
    private readonly List<AccessBag> _bags;

    private GateBuilder(List<AccessBag> bags) => _bags = bags;

    internal static GateBuilder Single(AccessBag bag) => new(new List<AccessBag> { bag });

    /// <summary>Add an alternative bag (OR).</summary>
    public GateBuilder Or(GateBuilder other)
    {
        _bags.AddRange(other._bags);
        return this;
    }

    /// <summary>Merge another single term into this builder's last bag (AND within one bag). The right operand must
    /// be a single term — AND-ing an OR-of-bags (which would need distribution) is rejected rather than silently
    /// dropping bags; build it explicitly with separate <see cref="Or"/> branches.</summary>
    public GateBuilder And(GateBuilder other)
    {
        if (other._bags.Count != 1)
        {
            throw new InvalidOperationException(
                "Gate.And requires a single-term right operand; distribute an OR-of-bags manually via separate Or(...) branches.");
        }
        var a = _bags[^1];
        var b = other._bags[^1];
        _bags[^1] = new AccessBag(
            a.IsRolesAnyOf.Concat(b.IsRolesAnyOf).ToArray(),
            a.HasAllOf.Concat(b.HasAllOf).ToArray(),
            a.RequiresOwner || b.RequiresOwner,
            a.Anyone || b.Anyone,
            a.Authenticated || b.Authenticated);
        return this;
    }

    public ActionGate Build() => new(_bags);

    public static implicit operator ActionGate(GateBuilder builder) => builder.Build();
}

/// <summary>SEC-0004 — the entry points for the fluent <see cref="GateBuilder"/>. Each returns a fresh builder.</summary>
public static class Gate
{
    /// <summary>The principal's role is this one (combine alternatives with <c>.Or</c>).</summary>
    public static GateBuilder Is(string role) => GateBuilder.Single(Bag(roles: new[] { role }));

    /// <summary>Holds an OAuth scope.</summary>
    public static GateBuilder HasScope(string scope) => WithGrant(new Grant.Scope(scope));

    /// <summary>Holds a role as a typed grant.</summary>
    public static GateBuilder HasRole(string role) => WithGrant(new Grant.Role(role));

    /// <summary>Holds a claim of <paramref name="type"/> equal to <paramref name="value"/>.</summary>
    public static GateBuilder HasClaim(string type, string value) => WithGrant(new Grant.Claim(type, value));

    /// <summary>Satisfies the entity's Owner predicate (row-bound from Slice C; "authenticated" at the coarse gate).</summary>
    public static GateBuilder Owner => GateBuilder.Single(Bag(requiresOwner: true));

    /// <summary>Any signed-in principal.</summary>
    public static GateBuilder Authenticated => GateBuilder.Single(Bag());

    /// <summary>Everyone — authenticated or not.</summary>
    public static GateBuilder Anyone => GateBuilder.Single(AccessBag.AnyoneBag);

    private static GateBuilder WithGrant(Grant grant) => GateBuilder.Single(Bag(grants: new[] { grant }));

    private static AccessBag Bag(IReadOnlyList<string>? roles = null, IReadOnlyList<Grant>? grants = null, bool requiresOwner = false)
        => new(roles ?? Array.Empty<string>(), grants ?? Array.Empty<Grant>(), requiresOwner, Anyone: false, Authenticated: true);
}
