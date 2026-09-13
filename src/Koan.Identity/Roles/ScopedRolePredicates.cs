using System.Collections.Frozen;
using System.Globalization;

namespace Koan.Identity.Roles;

/// <summary>An immutable, compiled set of namespaced memberships for one subject at one target scope.</summary>
public sealed class ScopedRoleMembershipSet
{
    private static readonly FrozenSet<string> NoTokens = Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenSet<string> _tokens;

    private ScopedRoleMembershipSet(string subject, bool isAuthenticated, IEnumerable<string> tokens, bool allowDerived)
    {
        StableSubject = isAuthenticated ? ScopedRoleScopeRef.Require(subject, nameof(subject)) : "anonymous";
        IsAuthenticated = isAuthenticated;
        _tokens = tokens.Select(token => NormalizeMembershipToken(token, allowDerived))
            .ToFrozenSet(StringComparer.Ordinal);
    }

    public static ScopedRoleMembershipSet Empty { get; } = new("anonymous", false, NoTokens, true);

    /// <summary>Creates an application-owned role/group membership set. Permission tokens are compiler output only.</summary>
    public static ScopedRoleMembershipSet Create(params string[] tokens)
        => new("external", true, tokens ?? throw new ArgumentNullException(nameof(tokens)), false);

    /// <summary>The immutable namespaced tokens. Compiled sets can include derived <c>permission:*</c> entries.</summary>
    public IReadOnlySet<string> Tokens => _tokens;
    public int Count => _tokens.Count;
    public bool Contains(string token) => _tokens.Contains(token);
    public bool ContainsAny(string first, string second) => _tokens.Contains(first) || _tokens.Contains(second);
    public bool ContainsAny(string first, string second, string third)
        => _tokens.Contains(first) || _tokens.Contains(second) || _tokens.Contains(third);
    public bool ContainsAny(ReadOnlySpan<string> tokens)
    {
        foreach (var token in tokens)
            if (_tokens.Contains(token)) return true;
        return false;
    }

    internal string StableSubject { get; }
    internal bool IsAuthenticated { get; }

    internal static ScopedRoleMembershipSet Compile(string subject, bool isAuthenticated,
        IEnumerable<string> roleIds, IEnumerable<string> capabilities)
        => new(subject, isAuthenticated,
            roleIds.Select(ScopedRoleTokens.RoleOrGroup)
                .Concat(capabilities.Select(ScopedRoleTokens.Permission)), true);

    private static string NormalizeMembershipToken(string token, bool allowDerived)
    {
        token = ScopedRoleScopeRef.Require(token, nameof(token));
        if (token.StartsWith("permission:", StringComparison.Ordinal))
        {
            if (allowDerived) return token;
            throw new ArgumentException("permission:* tokens are derived from role grants and cannot be supplied as membership.", nameof(token));
        }
        if (token.StartsWith("role:", StringComparison.Ordinal) || token.StartsWith("group:", StringComparison.Ordinal))
            return token;
        throw new ArgumentException("Membership tokens must use the role:* or group:* namespace.", nameof(token));
    }
}

/// <summary>An immutable compiled audience predicate matched against a subject's memberships.</summary>
public sealed class ScopedRoleAudience
{
    private readonly string[] _literalTokens;
    private readonly ScopedRoleAudienceClause[] _clauses;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _roleMembers;
    private readonly bool _allowsAnonymous;

    private ScopedRoleAudience(IEnumerable<string> literalTokens, IEnumerable<ScopedRoleAudienceClause> clauses,
        IReadOnlyDictionary<string, IReadOnlySet<string>> roleMembers, bool allowsAnonymous)
    {
        _literalTokens = literalTokens.Select(ScopedRoleTokens.RequireAudienceToken).Distinct(StringComparer.Ordinal).ToArray();
        _clauses = clauses.Select(clause => new ScopedRoleAudienceClause(clause.Kind, clause.Value,
            clause.Conditions?.ToArray())).ToArray();
        _roleMembers = roleMembers;
        _allowsAnonymous = allowsAnonymous;
        Tokens = _literalTokens.Concat(_clauses.Select(ScopedRoleTokens.Audience))
            .Where(token => token is not null).Select(token => token!)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    public static ScopedRoleAudience None { get; } = new([], [],
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal), false);

    /// <summary>Creates an unconditional role/group audience predicate.</summary>
    public static ScopedRoleAudience Any(params string[] tokens)
        => new(tokens ?? throw new ArgumentNullException(nameof(tokens)), [],
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal), false);

    public IReadOnlySet<string> Tokens { get; }
    public bool IsConditional => _clauses.Any(clause => clause.Conditions is { Count: > 0 });

    public bool Matches(ScopedRoleMembershipSet memberships,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        foreach (var token in _literalTokens)
            if (memberships.Contains(token)) return true;
        foreach (var clause in _clauses)
        {
            if (!ConditionsMatch(clause.Conditions, parameters)) continue;
            var matched = clause.Kind switch
            {
                ScopedRoleAudienceKind.Anonymous => !memberships.IsAuthenticated && _allowsAnonymous,
                ScopedRoleAudienceKind.Authenticated => memberships.IsAuthenticated,
                ScopedRoleAudienceKind.Subject => memberships.IsAuthenticated &&
                    StringComparer.Ordinal.Equals(memberships.StableSubject, clause.Value),
                ScopedRoleAudienceKind.Role => memberships.IsAuthenticated && clause.Value is { } roleId &&
                    (_roleMembers.TryGetValue(roleId, out var members)
                        ? members.Contains(memberships.StableSubject)
                        : memberships.Contains(ScopedRoleTokens.RoleOrGroup(roleId))),
                _ => false,
            };
            if (matched) return true;
        }
        return false;
    }

    internal static ScopedRoleAudience Compile(IEnumerable<ScopedRoleAudienceClause> clauses,
        IReadOnlyDictionary<string, IReadOnlySet<string>> roleMembers, bool allowsAnonymous)
        => new([], clauses, roleMembers, allowsAnonymous);

    internal static bool ConditionsMatch(IReadOnlyList<ScopedRoleCondition>? conditions,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        if (conditions is null || conditions.Count == 0) return true;
        if (parameters is null) return false;
        foreach (var condition in conditions)
        {
            if (!parameters.TryGetValue(condition.Parameter, out var actual) || actual is null) return false;
            var actualText = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "";
            var matched = condition.Operator switch
            {
                ScopedRoleConditionOperator.Equal => StringComparer.Ordinal.Equals(actualText, condition.Value),
                ScopedRoleConditionOperator.LessThanOrEqual => CompareDecimal(actualText, condition.Value, (a, b) => a <= b),
                ScopedRoleConditionOperator.GreaterThanOrEqual => CompareDecimal(actualText, condition.Value, (a, b) => a >= b),
                _ => false,
            };
            if (!matched) return false;
        }
        return true;
    }

    private static bool CompareDecimal(string actual, string expected, Func<decimal, decimal, bool> compare)
        => decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) &&
           decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var b) && compare(a, b);
}

internal static class ScopedRoleTokens
{
    internal static string RoleOrGroup(string roleId)
    {
        roleId = ScopedRoleScopeRef.Require(roleId, nameof(roleId));
        return roleId.StartsWith("role:", StringComparison.Ordinal) || roleId.StartsWith("group:", StringComparison.Ordinal)
            ? roleId : $"role:{roleId}";
    }

    internal static string Permission(string capability)
        => $"permission:{ScopedRoleScopeRef.Require(capability, nameof(capability))}";

    internal static string RequireAudienceToken(string token)
    {
        token = ScopedRoleScopeRef.Require(token, nameof(token));
        return token.StartsWith("role:", StringComparison.Ordinal) || token.StartsWith("group:", StringComparison.Ordinal)
            ? token : throw new ArgumentException("Audience tokens must use the role:* or group:* namespace.", nameof(token));
    }

    internal static string? Audience(ScopedRoleAudienceClause clause) => clause.Kind switch
    {
        ScopedRoleAudienceKind.Role when clause.Value is not null => RoleOrGroup(clause.Value),
        _ => null,
    };
}
