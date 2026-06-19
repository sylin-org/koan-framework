using System;
using System.Collections.Generic;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — the ONE parser from the <c>[Access]</c> string surface to the <see cref="AccessGate"/> model.
/// Static (pure, no state). Each action value is a comma-separated OR-list of SINGLE-term bags: the comma is the
/// only separator and the only OR; there is no in-string AND (that is structurally impossible — the §102
/// mitigation). Anything needing AND-within-a-bag drops to the Slice B fluent builder. Unknown or malformed tokens
/// fail fast with a specific, remediation-pointing <see cref="AccessGateException"/>.
/// </summary>
public static class AccessGateParser
{
    // Characters that signal an attempted AND / grouping inside one term — explicitly unsupported in the string.
    private static readonly char[] AndLikeChars = { ' ', '\t', '&', '|', '(', ')' };

    /// <summary>Build the whole-entity gate from the four <c>[Access]</c> params. For each of read/write/remove the
    /// value is the explicit param if non-null, else <paramref name="all"/> if non-null, else absent (open).</summary>
    public static AccessGate Parse(string entityName, string? read, string? write, string? remove, string? all)
    {
        var byAction = new Dictionary<string, ActionGate>(StringComparer.OrdinalIgnoreCase);
        AddAction(byAction, entityName, EntityAuthorizeActions.Read, read ?? all);
        AddAction(byAction, entityName, EntityAuthorizeActions.Write, write ?? all);
        AddAction(byAction, entityName, EntityAuthorizeActions.Remove, remove ?? all);
        return new AccessGate(byAction, new Dictionary<string, ActionGate>(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddAction(IDictionary<string, ActionGate> byAction, string entityName, string action, string? value)
    {
        if (value is null) return; // unspecified → open (allow-by-default); leave the action out of the map
        byAction[action] = ParseValue(value, entityName, action);
    }

    /// <summary>Parse one action value into an <see cref="ActionGate"/> (an OR-list of single-term bags).</summary>
    public static ActionGate ParseValue(string raw, string entityName, string action)
    {
        var bags = new List<AccessBag>();
        foreach (var piece in raw.Split(','))
        {
            var term = piece.Trim();
            if (term.Length == 0)
            {
                throw Error(entityName, action, raw, piece, "empty term — remove the stray comma");
            }
            if (term.IndexOfAny(AndLikeChars) >= 0)
            {
                throw Error(entityName, action, raw, term,
                    "[Access] values are single-term OR-lists; 'AND'/grouping requires the fluent Gate — " +
                    $"override EntityAccess<{entityName}> and write e.g. Gate.Is(\"member\").And(Gate.Has(\"scope:x\"))");
            }
            bags.Add(Classify(term, entityName, action, raw));
        }
        return new ActionGate(bags);
    }

    private static AccessBag Classify(string term, string entityName, string action, string raw)
    {
        if (Eq(term, "anyone")) return AccessBag.AnyoneBag;
        if (Eq(term, "authenticated")) return Bag();
        if (Eq(term, "owner")) return Bag(requiresOwner: true);

        if (StartsWith(term, "is:"))
        {
            var role = term.Substring(3).Trim();
            if (role.Length == 0) throw Error(entityName, action, raw, term, "empty role in 'is:'");
            return Bag(roles: new[] { role });
        }

        if (StartsWith(term, "has:"))
        {
            var rest = term.Substring(4);
            if (StartsWith(rest, "scope:"))
            {
                var v = rest.Substring(6).Trim();
                if (v.Length == 0) throw Error(entityName, action, raw, term, "empty grant value in 'has:scope:'");
                return Bag(grants: new Grant[] { new Grant.Scope(v) });
            }
            if (StartsWith(rest, "role:"))
            {
                var v = rest.Substring(5).Trim();
                if (v.Length == 0) throw Error(entityName, action, raw, term, "empty grant value in 'has:role:'");
                return Bag(grants: new Grant[] { new Grant.Role(v) });
            }
            if (StartsWith(rest, "claim:"))
            {
                var kv = rest.Substring(6);
                var eq = kv.IndexOf('=');
                if (eq <= 0 || eq >= kv.Length - 1)
                {
                    throw Error(entityName, action, raw, term, $"claim grant needs key=value; got '{term}'");
                }
                var key = kv.Substring(0, eq).Trim();
                var val = kv.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0)
                {
                    throw Error(entityName, action, raw, term, $"claim grant needs a non-empty key and value; got '{term}'");
                }
                return Bag(grants: new Grant[] { new Grant.Claim(key, val) });
            }
            throw Error(entityName, action, raw, term, $"unknown grant '{term}'; expected has:scope: / has:role: / has:claim:");
        }

        // Bare word that looks like a role is the most common mistake — suggest the fix.
        throw Error(entityName, action, raw, term, $"unknown token '{term}' — bare names are not allowed; did you mean 'is:{term}'?");
    }

    private static AccessBag Bag(
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<Grant>? grants = null,
        bool requiresOwner = false)
        => new(
            roles ?? Array.Empty<string>(),
            grants ?? Array.Empty<Grant>(),
            RequiresOwner: requiresOwner,
            Anyone: false,
            // every non-`anyone` term implies authentication (you can't hold a role/scope/claim or own a row anonymously)
            Authenticated: true);

    private static bool Eq(string term, string token) => string.Equals(term, token, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string term, string prefix) => term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static AccessGateException Error(string entityName, string action, string raw, string token, string reason)
    {
        var offset = raw.IndexOf(token, StringComparison.Ordinal);
        var at = offset >= 0 ? $" at offset {offset}" : "";
        return new AccessGateException(
            $"[Access] on {entityName}.{action}: {reason}{at}. Value: \"{raw}\".");
    }
}
