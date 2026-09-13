namespace Koan.Identity.Roles;

using System.Linq.Expressions;

public sealed class ScopedRoleCatalogBuilder
{
    private readonly Dictionary<string, ScopedRoleScopeDescriptor> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScopedRoleCapabilityDescriptor> _capabilities = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ScopedRoleResourceDescriptor> _resources = [];

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

    /// <summary>Enroll an Entity in scoped enforcement using one direct, provider-pushable scope-key field.</summary>
    public ScopedRoleResourceBuilder<TEntity> Resource<TEntity>(string scopeType,
        Expression<Func<TEntity, string>> scopeField) where TEntity : class
    {
        scopeType = ScopedRoleScopeRef.Require(scopeType, nameof(scopeType));
        var member = scopeField.Body switch
        {
            MemberExpression direct when direct.Expression == scopeField.Parameters[0] => direct.Member.Name,
            UnaryExpression { Operand: MemberExpression direct } when direct.Expression == scopeField.Parameters[0] => direct.Member.Name,
            _ => throw new InvalidOperationException("A scoped-role resource field must be one direct string property (for example, x => x.TopicId).")
        };
        return new(this, scopeType, member);
    }

    internal void RegisterResource<TEntity>(string scopeType, string scopeField,
        IReadOnlyDictionary<string, string> actions) where TEntity : class
    {
        _resources[typeof(TEntity)] = new(typeof(TEntity).FullName ?? typeof(TEntity).Name,
            scopeType, scopeField, new Dictionary<string, string>(actions, StringComparer.Ordinal));
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
        foreach (var resource in _resources.Values)
        {
            if (!_scopes.ContainsKey(resource.ScopeType))
                throw new InvalidOperationException($"Resource '{resource.Entity}' names unknown scope type '{resource.ScopeType}'.");
            foreach (var capability in resource.Actions.Values)
            {
                if (!_capabilities.TryGetValue(capability, out var descriptor) || !descriptor.ScopeTypes.Contains(resource.ScopeType))
                    throw new InvalidOperationException($"Resource '{resource.Entity}' action names capability '{capability}' which is unavailable for scope '{resource.ScopeType}'.");
            }
        }
        return new ScopedRoleCatalog(_scopes.Values.ToArray(), _capabilities.Values.ToArray(),
            _resources.ToDictionary(x => x.Key, x => x.Value));
    }
}

public sealed class ScopedRoleResourceBuilder<TEntity> where TEntity : class
{
    private readonly ScopedRoleCatalogBuilder _catalog;
    private readonly string _scopeType;
    private readonly string _scopeField;
    private readonly Dictionary<string, string> _actions = new(StringComparer.Ordinal);

    internal ScopedRoleResourceBuilder(ScopedRoleCatalogBuilder catalog, string scopeType, string scopeField)
    {
        _catalog = catalog;
        _scopeType = scopeType;
        _scopeField = scopeField;
    }

    public ScopedRoleResourceBuilder<TEntity> Read(string capability) => Action(ScopedRoleResourceActions.Read, capability);
    public ScopedRoleResourceBuilder<TEntity> Create(string capability) => Action(ScopedRoleResourceActions.Create, capability);
    public ScopedRoleResourceBuilder<TEntity> Update(string capability) => Action(ScopedRoleResourceActions.Update, capability);
    public ScopedRoleResourceBuilder<TEntity> Delete(string capability) => Action(ScopedRoleResourceActions.Delete, capability);

    public ScopedRoleResourceBuilder<TEntity> Action(string action, string capability)
    {
        action = ScopedRoleScopeRef.Require(action, nameof(action));
        capability = ScopedRoleScopeRef.Require(capability, nameof(capability));
        if (!_actions.TryAdd(action, capability))
            throw new InvalidOperationException($"Resource action '{action}' is declared more than once for {typeof(TEntity).Name}.");
        _catalog.RegisterResource<TEntity>(_scopeType, _scopeField, _actions);
        return this;
    }
}

public sealed class ScopedRoleCatalog
{
    private readonly IReadOnlyDictionary<string, ScopedRoleScopeDescriptor> _scopes;
    private readonly IReadOnlyDictionary<string, ScopedRoleCapabilityDescriptor> _capabilities;
    private readonly IReadOnlyDictionary<Type, ScopedRoleResourceDescriptor> _resources;

    public ScopedRoleCatalog(IEnumerable<IScopedRoleCatalogContributor> contributors)
    {
        var builder = new ScopedRoleCatalogBuilder();
        foreach (var contributor in contributors) contributor.Describe(builder);
        var compiled = builder.Build();
        _scopes = compiled._scopes;
        _capabilities = compiled._capabilities;
        _resources = compiled._resources;
    }

    internal ScopedRoleCatalog(
        IReadOnlyList<ScopedRoleScopeDescriptor> scopes,
        IReadOnlyList<ScopedRoleCapabilityDescriptor> capabilities,
        IReadOnlyDictionary<Type, ScopedRoleResourceDescriptor> resources)
    {
        _scopes = scopes.ToDictionary(x => x.Type, StringComparer.Ordinal);
        _capabilities = capabilities.ToDictionary(x => x.Key, StringComparer.Ordinal);
        _resources = resources;
    }

    public IReadOnlyList<ScopedRoleCapabilityDescriptor> Capabilities => _capabilities.Values.ToArray();
    public IReadOnlyList<ScopedRoleScopeDescriptor> Scopes => _scopes.Values.ToArray();
    public IReadOnlyList<ScopedRoleResourceDescriptor> Resources => _resources.Values.ToArray();

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

    public ScopedRoleResourceDescriptor DemandResource<TEntity>(string action, string targetScopeType)
        where TEntity : class
    {
        if (!_resources.TryGetValue(typeof(TEntity), out var resource))
            throw new ScopedRoleValidationException("resource.unenrolled", $"Entity '{typeof(TEntity).Name}' is not enrolled in scoped-role enforcement.");
        if (!StringComparer.Ordinal.Equals(resource.ScopeType, targetScopeType))
            throw new ScopedRoleValidationException("resource.scope.mismatch", $"Entity '{typeof(TEntity).Name}' is not mapped to scope type '{targetScopeType}'.");
        if (!resource.Actions.TryGetValue(action, out _))
            throw new ScopedRoleValidationException("resource.action.unsupported", $"Action '{action}' is not declared for entity '{typeof(TEntity).Name}'.");
        return resource;
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
