namespace Koan.Identity.Roles;

public sealed class ScopedRoleCatalogBuilder
{
    private readonly Dictionary<string, ScopedRoleScopeDescriptor> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScopedRoleCapabilityDescriptor> _capabilities = new(StringComparer.Ordinal);

    public ScopedRoleCatalogBuilder Scope(string type, string? parentType = null)
    {
        type = ScopedRoleScopeRef.Require(type, nameof(type));
        if (!_scopes.TryAdd(type, new(type, string.IsNullOrWhiteSpace(parentType) ? null : parentType.Trim())))
            throw new InvalidOperationException($"Scoped-role scope type '{type}' is declared more than once.");
        return this;
    }

    public ScopedRoleCatalogBuilder Capability(
        string key,
        IEnumerable<string> scopeTypes,
        string? label = null,
        string? description = null,
        bool allowsAnonymous = false,
        IEnumerable<string>? parameters = null)
    {
        key = ScopedRoleScopeRef.Require(key, nameof(key));
        var scopes = scopeTypes.Select(x => ScopedRoleScopeRef.Require(x, nameof(scopeTypes)))
            .ToHashSet(StringComparer.Ordinal);
        if (scopes.Count == 0) throw new InvalidOperationException($"Capability '{key}' must name at least one scope type.");
        var descriptor = new ScopedRoleCapabilityDescriptor(key, scopes, label, description, allowsAnonymous,
            parameters?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal));
        if (!_capabilities.TryAdd(key, descriptor))
            throw new InvalidOperationException($"Scoped-role capability '{key}' is declared more than once.");
        return this;
    }

    internal ScopedRoleCatalog Build()
    {
        foreach (var scope in _scopes.Values)
            if (scope.ParentType is { } parent && !_scopes.ContainsKey(parent))
                throw new InvalidOperationException($"Scope type '{scope.Type}' names unknown parent type '{parent}'.");
        foreach (var capability in _capabilities.Values)
            foreach (var scope in capability.ScopeTypes)
                if (!_scopes.ContainsKey(scope))
                    throw new InvalidOperationException($"Capability '{capability.Key}' names unknown scope type '{scope}'.");
        return new ScopedRoleCatalog(_scopes.Values.ToArray(), _capabilities.Values.ToArray());
    }
}

public sealed class ScopedRoleCatalog
{
    private readonly IReadOnlyDictionary<string, ScopedRoleScopeDescriptor> _scopes;
    private readonly IReadOnlyDictionary<string, ScopedRoleCapabilityDescriptor> _capabilities;

    public ScopedRoleCatalog(IEnumerable<IScopedRoleCatalogContributor> contributors)
    {
        var builder = new ScopedRoleCatalogBuilder();
        foreach (var contributor in contributors) contributor.Describe(builder);
        var compiled = builder.Build();
        _scopes = compiled._scopes;
        _capabilities = compiled._capabilities;
    }

    internal ScopedRoleCatalog(
        IReadOnlyList<ScopedRoleScopeDescriptor> scopes,
        IReadOnlyList<ScopedRoleCapabilityDescriptor> capabilities)
    {
        _scopes = scopes.ToDictionary(x => x.Type, StringComparer.Ordinal);
        _capabilities = capabilities.ToDictionary(x => x.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<ScopedRoleCapabilityDescriptor> Capabilities => _capabilities.Values.ToArray();
    public IReadOnlyList<ScopedRoleScopeDescriptor> Scopes => _scopes.Values.ToArray();

    public ScopedRoleScopeDescriptor DemandScope(string type)
        => _scopes.TryGetValue(type, out var value) ? value
            : throw new ScopedRoleValidationException("scope.unknown", $"Scope type '{type}' is not declared.");

    public ScopedRoleCapabilityDescriptor DemandCapability(string key, string scopeType)
    {
        if (!_capabilities.TryGetValue(key, out var value))
            throw new ScopedRoleValidationException("capability.unknown", $"Capability '{key}' is not declared.");
        if (!value.ScopeTypes.Contains(scopeType))
            throw new ScopedRoleValidationException("capability.scope.unsupported", $"Capability '{key}' is not valid for scope type '{scopeType}'.");
        return value;
    }
}

public class ScopedRoleException : InvalidOperationException
{
    public ScopedRoleException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed class ScopedRoleValidationException : ScopedRoleException
{
    public ScopedRoleValidationException(string code, string message) : base(code, message) { }
}

public sealed class ScopedRoleAuthorizationException : ScopedRoleException
{
    public ScopedRoleAuthorizationException(string code, string message) : base(code, message) { }
}

public sealed class ScopedRoleConcurrencyException : ScopedRoleException
{
    public ScopedRoleConcurrencyException(string message) : base("version.stale", message) { }
}
