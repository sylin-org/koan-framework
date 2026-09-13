using System.Globalization;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

/// <summary>Domain-neutral owner for guarded scoped-role persistence, resolution and explanation.</summary>
public sealed class RoleEngine
{
    private readonly ScopedRoleCatalog _catalog;
    private readonly IReadOnlyList<IScopedRoleAuthorityContributor> _authorities;
    private readonly IReadOnlyList<IScopedRoleGuardContributor> _guards;
    private readonly RoleEngineOptions _options;
    private readonly TimeProvider _clock;
    private readonly IIdentityActorAccessor? _actorAccessor;
    private readonly IScopedRoleSubjectAccessor? _subjectAccessor;

    public RoleEngine(
        ScopedRoleCatalog catalog,
        IEnumerable<IScopedRoleAuthorityContributor> authorities,
        IEnumerable<IScopedRoleGuardContributor> guards,
        IOptions<RoleEngineOptions> options,
        IIdentityActorAccessor? actorAccessor = null,
        IScopedRoleSubjectAccessor? subjectAccessor = null,
        TimeProvider? clock = null)
    {
        _catalog = catalog;
        _authorities = authorities.ToArray();
        _guards = guards.ToArray();
        _options = options.Value;
        _actorAccessor = actorAccessor;
        _subjectAccessor = subjectAccessor;
        _clock = clock ?? TimeProvider.System;
        if (_options.MaxAncestryDepth is < 1 or > 128) throw new InvalidOperationException("Scoped-role ancestry bound must be between 1 and 128.");
        if (_options.MaxBindingsPerSubject is < 1 or > 10_000) throw new InvalidOperationException("Scoped-role binding bound must be between 1 and 10000.");
        if (_options.MaxPoliciesPerTenant is < 1 or > 10_000) throw new InvalidOperationException("Scoped-role policy bound must be between 1 and 10000.");
        if (_options.MaxClausesPerRecord is < 1 or > 1024) throw new InvalidOperationException("Scoped-role clause bound must be between 1 and 1024.");
    }

    public Task<IReadOnlyList<ScopedRoleCapabilityDescriptor>> Describe(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_catalog.Capabilities);
    }

    public Task<ScopedRoleScope> Register(RegisterScopedRoleScope command, CancellationToken ct = default)
        => Register(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRoleScope> Register(ScopedRoleActor actor, RegisterScopedRoleScope command, CancellationToken ct = default)
    {
        var target = Normalize(command.Scope);
        var descriptor = _catalog.DemandScope(target.Type);
        var parent = command.Parent is null ? null : Normalize(command.Parent);
        if (parent is not null && parent.TenantId != target.TenantId)
            throw new ScopedRoleValidationException("scope.tenant.mismatch", "A scope parent must belong to the same tenant.");
        if (!StringComparer.Ordinal.Equals(descriptor.ParentType, parent?.Type))
            throw new ScopedRoleValidationException("scope.parent.invalid", $"Scope type '{target.Type}' requires parent type '{descriptor.ParentType ?? "<none>"}'.");
        var parentAncestry = parent is null ? (IReadOnlyList<ScopedRoleScopeRef>)[target] : await LoadAncestry(parent, ct).ConfigureAwait(false);
        if (parent is not null && ContainsScope(parentAncestry, target))
            throw new ScopedRoleValidationException("scope.cycle", "A scope cannot be moved beneath one of its descendants.");

        await Demand(new(actor, ScopedRoleAuthorityOperation.RegisterScope, target), parentAncestry, ct).ConfigureAwait(false);

        var id = ScopedRoleScope.KeyFor(target);
        var current = await ScopedRoleScope.Get(id, ct).ConfigureAwait(false);
        if (current is null)
        {
            if (command.ExpectedVersion is not null)
                throw new ScopedRoleConcurrencyException("A version was supplied for a scope that does not exist.");
            var created = new ScopedRoleScope
            {
                Id = id, TenantId = target.TenantId, Type = target.Type, ScopeId = target.Id,
                ParentType = parent?.Type, ParentScopeId = parent?.Id, Version = 1,
                UpdatedAt = Now, UpdatedBy = actor.StableSubject,
            };
            return await Insert(created, ct).ConfigureAwait(false);
        }

        if (command.ExpectedVersion != current.Version)
            throw new ScopedRoleConcurrencyException("The scope changed after it was read.");
        current.ParentType = parent?.Type;
        current.ParentScopeId = parent?.Id;
        current.Version++;
        current.UpdatedAt = Now;
        current.UpdatedBy = actor.StableSubject;
        await Replace(current, command.ExpectedVersion.Value, ct).ConfigureAwait(false);
        return current;
    }

    public Task<ScopedRoleDefinition> Define(DefineScopedRole command, CancellationToken ct = default)
        => Define(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRoleDefinition> Define(ScopedRoleActor actor, DefineScopedRole command, CancellationToken ct = default)
    {
        var scope = Normalize(command.Scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var grants = NormalizeGrants(command.Grants, scope.Type);
        var id = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N") : command.Id.Trim();
        await Demand(new(actor, ScopedRoleAuthorityOperation.DefineRole, scope, RoleId: id,
            EffectiveCapabilities: grants.Select(x => x.Capability).ToHashSet(StringComparer.Ordinal)), ancestry, ct).ConfigureAwait(false);

        var role = new ScopedRoleDefinition
        {
            Id = id, TenantId = scope.TenantId, ScopeType = scope.Type, ScopeId = scope.Id,
            Name = ScopedRoleScopeRef.Require(command.Name, nameof(command.Name)), Purpose = command.Purpose?.Trim(),
            Grants = grants, Presentation = Copy(command.Presentation), Version = 1, AuthorityVersion = 1,
            Status = ScopedRoleStatus.Active, UpdatedAt = Now, UpdatedBy = actor.StableSubject,
        };
        return await Insert(role, ct).ConfigureAwait(false);
    }

    public Task<ScopedRoleDefinition> Edit(EditScopedRole command, CancellationToken ct = default)
        => Edit(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRoleDefinition> Edit(ScopedRoleActor actor, EditScopedRole command, CancellationToken ct = default)
    {
        var role = await DemandRole(command.RoleId, ct).ConfigureAwait(false);
        var scope = role.Scope();
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        if (role.Version != command.ExpectedVersion)
            throw new ScopedRoleConcurrencyException("The role changed after it was read.");

        var grants = command.Grants is null ? role.Grants : NormalizeGrants(command.Grants, scope.Type);
        var status = command.Status ?? role.Status;
        var authorityChanged = status != role.Status || !GrantSetsEqual(role.Grants, grants);
        var approval = await ApprovalFor(role.Id, role.TenantId, grants, ct).ConfigureAwait(false);
        await Demand(new(actor, ScopedRoleAuthorityOperation.EditRole, scope, RoleId: role.Id,
            EffectiveCapabilities: approval.Capabilities), ancestry, ct).ConfigureAwait(false);

        role.Name = command.Name is null ? role.Name : ScopedRoleScopeRef.Require(command.Name, nameof(command.Name));
        role.Purpose = command.Purpose ?? role.Purpose;
        role.Presentation = command.Presentation is null ? role.Presentation : Copy(command.Presentation);
        role.Grants = grants;
        role.Status = status;
        role.Version++;
        if (authorityChanged) role.AuthorityVersion++;
        role.UpdatedAt = Now;
        role.UpdatedBy = actor.StableSubject;
        await Replace(role, command.ExpectedVersion, ct).ConfigureAwait(false);
        return role;
    }

    public Task<ScopedRoleDefinition> Disable(string roleId, long expectedVersion, CancellationToken ct = default)
        => Disable(CurrentAdministrativeActor(), roleId, expectedVersion, ct);

    internal Task<ScopedRoleDefinition> Disable(ScopedRoleActor actor, string roleId, long expectedVersion, CancellationToken ct = default)
        => Edit(actor, new(roleId, expectedVersion, Status: ScopedRoleStatus.Disabled), ct);

    public Task<ScopedRoleDefinition> Retire(string roleId, long expectedVersion, CancellationToken ct = default)
        => Retire(CurrentAdministrativeActor(), roleId, expectedVersion, ct);

    internal Task<ScopedRoleDefinition> Retire(ScopedRoleActor actor, string roleId, long expectedVersion, CancellationToken ct = default)
        => Edit(actor, new(roleId, expectedVersion, Status: ScopedRoleStatus.Retired), ct);

    public Task<ScopedRoleBinding> Assign(AssignScopedRole command, CancellationToken ct = default)
        => Assign(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRoleBinding> Assign(ScopedRoleActor actor, AssignScopedRole command, CancellationToken ct = default)
    {
        var scope = Normalize(command.Scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var role = await DemandRole(command.RoleId, ct).ConfigureAwait(false);
        DemandSameTenant(scope.TenantId, role.TenantId);
        if (role.Status != ScopedRoleStatus.Active)
            throw new ScopedRoleValidationException("role.inactive", "Only an active role can be assigned.");
        if (!ContainsScope(ancestry, role.Scope()))
            throw new ScopedRoleValidationException("role.scope.invalid", "The role is not defined at this scope or one of its ancestors.");
        if (command.ExpiresAt is { } expires && expires <= Now)
            throw new ScopedRoleValidationException("binding.expiry.invalid", "A new binding must expire in the future.");

        var approval = await ApprovalFor(role.Id, role.TenantId, role.Grants, ct).ConfigureAwait(false);
        var subject = ScopedRoleScopeRef.Require(command.Subject, nameof(command.Subject));
        await Demand(new(actor, ScopedRoleAuthorityOperation.AssignRole, scope, subject, role.Id,
            command.Propagation, command.ExpiresAt, approval.Capabilities), ancestry, ct).ConfigureAwait(false);

        var id = ScopedRoleBinding.KeyFor(scope.TenantId, subject, role.Id, scope);
        var binding = new ScopedRoleBinding
        {
            Id = id, TenantId = scope.TenantId, Subject = subject, RoleId = role.Id,
            ScopeType = scope.Type, ScopeId = scope.Id, Propagation = command.Propagation,
            ExpiresAt = command.ExpiresAt, ApprovedRoleVersion = role.AuthorityVersion,
            ApprovedPolicyVersions = approval.PolicyVersions, Version = 1,
            IssuedBy = actor.StableSubject, UpdatedBy = actor.StableSubject, UpdatedAt = Now,
        };
        try
        {
            return await Insert(binding, ct).ConfigureAwait(false);
        }
        catch (ScopedRoleConcurrencyException)
        {
            var existing = await ScopedRoleBinding.Get(id, ct).ConfigureAwait(false);
            if (existing is not null && !existing.Revoked && existing.TenantId == binding.TenantId &&
                existing.Subject == binding.Subject && existing.RoleId == binding.RoleId &&
                existing.ScopeType == binding.ScopeType && existing.ScopeId == binding.ScopeId &&
                existing.Propagation == binding.Propagation && existing.ExpiresAt == binding.ExpiresAt &&
                existing.ApprovedRoleVersion == binding.ApprovedRoleVersion &&
                DictionaryEqual(existing.ApprovedPolicyVersions, binding.ApprovedPolicyVersions))
                return existing; // idempotent retry; importantly does not renew expiry or rewrite attribution.
            throw;
        }
    }

    public Task<ScopedRoleBinding> Reapprove(string bindingId, long expectedVersion, CancellationToken ct = default)
        => Reapprove(CurrentAdministrativeActor(), bindingId, expectedVersion, ct);

    internal async Task<ScopedRoleBinding> Reapprove(ScopedRoleActor actor, string bindingId, long expectedVersion, CancellationToken ct = default)
    {
        var binding = await DemandBinding(bindingId, ct).ConfigureAwait(false);
        if (binding.Version != expectedVersion) throw new ScopedRoleConcurrencyException("The binding changed after it was read.");
        if (binding.Revoked || binding.ExpiresAt is { } expires && expires <= Now)
            throw new ScopedRoleValidationException("binding.inactive", "A revoked or expired binding cannot be reapproved.");
        var scope = binding.Scope();
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var role = await DemandRole(binding.RoleId, ct).ConfigureAwait(false);
        if (role.Status != ScopedRoleStatus.Active)
            throw new ScopedRoleValidationException("role.inactive", "An inactive role cannot be reapproved.");
        var approval = await ApprovalFor(role.Id, role.TenantId, role.Grants, ct).ConfigureAwait(false);
        await Demand(new(actor, ScopedRoleAuthorityOperation.AssignRole, scope, binding.Subject, role.Id,
            binding.Propagation, binding.ExpiresAt, approval.Capabilities), ancestry, ct).ConfigureAwait(false);
        binding.ApprovedRoleVersion = role.AuthorityVersion;
        binding.ApprovedPolicyVersions = approval.PolicyVersions;
        binding.Version++;
        binding.UpdatedAt = Now;
        binding.UpdatedBy = actor.StableSubject;
        await Replace(binding, expectedVersion, ct).ConfigureAwait(false);
        return binding;
    }

    public Task<ScopedRoleBinding> Revoke(string bindingId, long expectedVersion, CancellationToken ct = default)
        => Revoke(CurrentAdministrativeActor(), bindingId, expectedVersion, ct);

    internal async Task<ScopedRoleBinding> Revoke(ScopedRoleActor actor, string bindingId, long expectedVersion, CancellationToken ct = default)
    {
        var binding = await DemandBinding(bindingId, ct).ConfigureAwait(false);
        if (binding.Version != expectedVersion) throw new ScopedRoleConcurrencyException("The binding changed after it was read.");
        var ancestry = await LoadAncestry(binding.Scope(), ct).ConfigureAwait(false);
        await Demand(new(actor, ScopedRoleAuthorityOperation.RevokeRole, binding.Scope(), binding.Subject,
            binding.RoleId, binding.Propagation, binding.ExpiresAt), ancestry, ct).ConfigureAwait(false);
        binding.Revoked = true;
        binding.Version++;
        binding.UpdatedAt = Now;
        binding.UpdatedBy = actor.StableSubject;
        await Replace(binding, expectedVersion, ct).ConfigureAwait(false);
        return binding;
    }

    public Task<ScopedRolePolicy> Replace(ReplaceScopedRolePolicy command, CancellationToken ct = default)
        => Replace(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRolePolicy> Replace(ScopedRoleActor actor, ReplaceScopedRolePolicy command, CancellationToken ct = default)
    {
        var scope = Normalize(command.Scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var capability = _catalog.DemandCapability(command.Capability, scope.Type);
        var audience = NormalizeAudience(command.Audience, capability);
        var selectedRoles = audience.Where(x => x.Kind == ScopedRoleAudienceKind.Role).Select(x => x.Value!).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var roleId in selectedRoles)
        {
            var role = await DemandRole(roleId, ct).ConfigureAwait(false);
            DemandSameTenant(scope.TenantId, role.TenantId);
            if (role.Status != ScopedRoleStatus.Active || !ContainsScope(ancestry, role.Scope()))
                throw new ScopedRoleValidationException("policy.role.invalid", "A replacement names an inactive or inapplicable role.");
        }
        await Demand(new(actor, ScopedRoleAuthorityOperation.ManagePolicy, scope,
            EffectiveCapabilities: new HashSet<string>([capability.Key], StringComparer.Ordinal),
            EffectiveRoleIds: selectedRoles.ToHashSet(StringComparer.Ordinal)), ancestry, ct).ConfigureAwait(false);

        var id = ScopedRolePolicy.KeyFor(scope, capability.Key);
        var current = await ScopedRolePolicy.Get(id, ct).ConfigureAwait(false);
        if (current is null)
        {
            if (command.ExpectedVersion is not null)
                throw new ScopedRoleConcurrencyException("A version was supplied for a policy that does not exist.");
            return await Insert(new ScopedRolePolicy
            {
                Id = id, TenantId = scope.TenantId, ScopeType = scope.Type, ScopeId = scope.Id,
                Capability = capability.Key, Mode = ScopedRoleOverrideMode.Replace, Audience = audience,
                Version = 1, UpdatedAt = Now, UpdatedBy = actor.StableSubject,
            }, ct).ConfigureAwait(false);
        }
        if (command.ExpectedVersion != current.Version)
            throw new ScopedRoleConcurrencyException("The policy changed after it was read.");
        current.Mode = ScopedRoleOverrideMode.Replace;
        current.Audience = audience;
        current.Version++;
        current.UpdatedAt = Now;
        current.UpdatedBy = actor.StableSubject;
        await Replace(current, command.ExpectedVersion.Value, ct).ConfigureAwait(false);
        return current;
    }

    public Task<ScopedRolePolicy> Inherit(ScopedRoleScopeRef scope, string capability, long expectedVersion,
        CancellationToken ct = default)
        => Inherit(CurrentAdministrativeActor(), scope, capability, expectedVersion, ct);

    internal async Task<ScopedRolePolicy> Inherit(ScopedRoleActor actor, ScopedRoleScopeRef scope, string capability,
        long expectedVersion, CancellationToken ct = default)
    {
        scope = Normalize(scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        _catalog.DemandCapability(capability, scope.Type);
        await Demand(new(actor, ScopedRoleAuthorityOperation.ManagePolicy, scope,
            EffectiveCapabilities: new HashSet<string>([capability], StringComparer.Ordinal)), ancestry, ct).ConfigureAwait(false);
        var policy = await ScopedRolePolicy.Get(ScopedRolePolicy.KeyFor(scope, capability), ct).ConfigureAwait(false)
            ?? throw new ScopedRoleValidationException("policy.missing", "The policy does not exist.");
        if (policy.Version != expectedVersion) throw new ScopedRoleConcurrencyException("The policy changed after it was read.");
        policy.Mode = ScopedRoleOverrideMode.Inherit;
        policy.Audience = [];
        policy.Version++;
        policy.UpdatedAt = Now;
        policy.UpdatedBy = actor.StableSubject;
        await Replace(policy, expectedVersion, ct).ConfigureAwait(false);
        return policy;
    }

    public Task<bool> Check(string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        => Check(CurrentSubject(), capability, target, parameters, ct);

    internal async Task<bool> Check(ScopedRoleActor subject, string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        => (await Plan(subject, capability, target, parameters, ct).ConfigureAwait(false)).Allowed;

    public Task<ScopedRolePreview> Preview(string subject, string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        => Preview(CurrentAdministrativeActor(), new ScopedRoleActor(ScopedRoleScopeRef.Require(subject, nameof(subject))),
            capability, target, parameters, ct);

    internal async Task<ScopedRolePreview> Preview(ScopedRoleActor actor, ScopedRoleActor subject, string capability,
        ScopedRoleScopeRef target, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        target = Normalize(target);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        await Demand(new(actor, ScopedRoleAuthorityOperation.Preview, target, subject.StableSubject), ancestry, ct).ConfigureAwait(false);
        return new(await Plan(subject, capability, target, parameters, ct).ConfigureAwait(false));
    }

    public Task<ScopedRolePlan> Plan(string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        => Plan(CurrentSubject(), capability, target, parameters, ct);

    internal async Task<ScopedRolePlan> Plan(ScopedRoleActor subject, string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        target = Normalize(target);
        var descriptor = _catalog.DemandCapability(capability, target.Type);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var now = Now;
        var bindings = subject.IsAuthenticated
            ? await LoadBindings(subject.StableSubject, target.TenantId, ct).ConfigureAwait(false)
            : [];
        var applicable = bindings.Where(x => !x.Revoked && (x.ExpiresAt is null || x.ExpiresAt > now) &&
            (SameScope(x.Scope(), target) || x.Propagation == ScopedRolePropagation.Descendants && ContainsScope(ancestry, x.Scope())))
            .ToArray();
        var roles = new Dictionary<string, ScopedRoleDefinition>(StringComparer.Ordinal);
        foreach (var roleId in applicable.Select(x => x.RoleId).Distinct(StringComparer.Ordinal))
        {
            var role = await ScopedRoleDefinition.Get(roleId, ct).ConfigureAwait(false);
            if (role is not null && role.TenantId == target.TenantId) roles[role.Id] = role;
        }
        var live = applicable.Where(x => roles.TryGetValue(x.RoleId, out var role) &&
            role.Status == ScopedRoleStatus.Active && role.AuthorityVersion == x.ApprovedRoleVersion &&
            ContainsScope(ancestry, role.Scope())).ToArray();

        ScopedRolePolicy? winning = null;
        foreach (var scope in ancestry)
        {
            var policy = await ScopedRolePolicy.Get(ScopedRolePolicy.KeyFor(scope, capability), ct).ConfigureAwait(false);
            if (policy is { Mode: ScopedRoleOverrideMode.Replace }) { winning = policy; break; }
        }

        var reasons = new List<ScopedRoleReason>();
        bool allowed;
        if (winning is not null)
        {
            allowed = winning.Audience.Any(a => AudienceMatches(a, descriptor, subject, live, roles, winning, parameters));
            reasons.Add(new(allowed ? "policy.replace.matched" : "policy.replace.denied",
                allowed ? "The nearest replacement audience matched." : "The nearest replacement audience did not match.",
                nameof(ScopedRolePolicy), winning.Id));
        }
        else
        {
            allowed = live.Any(binding => roles[binding.RoleId].Grants.Any(g =>
                StringComparer.Ordinal.Equals(g.Capability, capability) && ConditionsMatch(g.Conditions, parameters)));
            reasons.Add(new(allowed ? "role.default.matched" : "role.default.denied",
                allowed ? "An applicable role grant matched." : "No applicable role grant matched."));
        }

        var versions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var binding in live) versions[$"binding:{binding.Id}"] = binding.Version;
        foreach (var role in roles.Values) versions[$"role:{role.Id}"] = role.Version;
        if (winning is not null) versions[$"policy:{winning.Id}"] = winning.Version;
        var safeParameters = parameters ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var guard in _guards)
        {
            var result = await guard.Evaluate(new(subject, target, capability, safeParameters), ct).ConfigureAwait(false);
            if (result.VersionKey is { } key && result.Version is { } version) versions[$"guard:{key}"] = version;
            if (!result.Allowed)
            {
                allowed = false;
                reasons.Add(new(result.Code, result.Message));
            }
        }
        return new(subject, target, capability, allowed, reasons,
            live.Select(x => x.RoleId).Distinct(StringComparer.Ordinal).ToArray(), winning?.Id, versions, now);
    }

    private async Task<IReadOnlyList<ScopedRoleScopeRef>> LoadAncestry(ScopedRoleScopeRef target, CancellationToken ct)
    {
        var result = new List<ScopedRoleScopeRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = target;
        for (var depth = 0; depth < _options.MaxAncestryDepth; depth++)
        {
            _catalog.DemandScope(current.Type);
            var key = ScopedRoleScope.KeyFor(current);
            if (!seen.Add(key)) throw new ScopedRoleValidationException("scope.cycle", "The trusted scope ancestry contains a cycle.");
            var row = await ScopedRoleScope.Get(key, ct).ConfigureAwait(false)
                ?? throw new ScopedRoleValidationException("scope.unknown", "The trusted target scope is not registered.");
            if (!SameScope(row.Reference(), current))
                throw new ScopedRoleValidationException("scope.identity.invalid", "The stored scope identity does not match its trusted key.");
            result.Add(current);
            if (row.ParentType is null && row.ParentScopeId is null) return result;
            if (row.ParentType is null || row.ParentScopeId is null)
                throw new ScopedRoleValidationException("scope.parent.invalid", "The stored scope parent is incomplete.");
            current = new(target.TenantId, row.ParentType, row.ParentScopeId);
        }
        throw new ScopedRoleValidationException("scope.depth.exceeded", "The trusted scope ancestry exceeds the configured bound.");
    }

    private async Task<IReadOnlyList<ScopedRoleBinding>> LoadBindings(string subject, string tenantId, CancellationToken ct)
    {
        var page = new QueryDefinition { Page = 1, PageSize = _options.MaxBindingsPerSubject + 1 };
        var rows = await ScopedRoleBinding.Query(x => x.TenantId == tenantId && x.Subject == subject, page, ct).ConfigureAwait(false);
        if (rows.Count > _options.MaxBindingsPerSubject)
            throw new ScopedRoleValidationException("bindings.bound.exceeded", "The subject has more scoped-role bindings than the configured evaluation bound.");
        return rows;
    }

    private async Task<RoleApproval> ApprovalFor(string roleId, string tenantId,
        IReadOnlyList<ScopedRoleGrantClause> grants, CancellationToken ct)
    {
        var result = grants.Select(x => x.Capability).ToHashSet(StringComparer.Ordinal);
        var versions = new Dictionary<string, long>(StringComparer.Ordinal);
        var page = new QueryDefinition { Page = 1, PageSize = _options.MaxPoliciesPerTenant + 1 };
        var policies = await ScopedRolePolicy.Query(x => x.TenantId == tenantId, page, ct).ConfigureAwait(false);
        if (policies.Count > _options.MaxPoliciesPerTenant)
            throw new ScopedRoleValidationException("policies.bound.exceeded", "The tenant has more scoped-role policies than the configured delegation bound.");
        foreach (var policy in policies)
            if (policy.Mode == ScopedRoleOverrideMode.Replace && policy.Audience.Any(x =>
                    x.Kind == ScopedRoleAudienceKind.Role && StringComparer.Ordinal.Equals(x.Value, roleId)))
            {
                result.Add(policy.Capability);
                versions[policy.Id] = policy.Version;
            }
        return new(result, versions);
    }

    private async Task Demand(ScopedRoleAuthorityRequest request, IReadOnlyList<ScopedRoleScopeRef> ancestry, CancellationToken ct)
    {
        if (!request.Actor.IsAuthenticated)
            throw new ScopedRoleAuthorizationException("authority.anonymous", "Authentication is required for scoped-role administration.");
        foreach (var contributor in _authorities)
        {
            var envelopes = await contributor.Contribute(request, ct).ConfigureAwait(false);
            if (envelopes.Any(x => Allows(x, request, ancestry))) return;
        }
        throw new ScopedRoleAuthorizationException("authority.denied", "The actor is not authorized for this scoped-role operation.");
    }

    private static bool Allows(ScopedRoleAuthorityEnvelope envelope, ScopedRoleAuthorityRequest request,
        IReadOnlyList<ScopedRoleScopeRef> ancestry)
    {
        if (!envelope.Operations.Contains(request.Operation)) return false;
        if (!SameScope(envelope.Scope, request.Target) && !(envelope.Descendants && ContainsScope(ancestry, envelope.Scope))) return false;
        if (request.RoleId is { } roleId && envelope.RoleIds is { } roleIds && !roleIds.Contains(roleId)) return false;
        if (request.Propagation is { } propagation && envelope.Propagations is { } propagations && !propagations.Contains(propagation)) return false;
        if (envelope.MaximumExpiry is { } maximum &&
            (request.ExpiresAt is not { } expiry || expiry > maximum)) return false;
        if (request.EffectiveCapabilities is { Count: > 0 } capabilities && envelope.Capabilities is { } allowed && !capabilities.All(allowed.Contains)) return false;
        if (request.EffectiveRoleIds is { Count: > 0 } roles && envelope.RoleIds is { } allowedRoles && !roles.All(allowedRoles.Contains)) return false;
        if (request.Operation == ScopedRoleAuthorityOperation.AssignRole &&
            StringComparer.Ordinal.Equals(request.Actor.StableSubject, request.Subject) && !envelope.AllowSelfAssignment) return false;
        return true;
    }

    private List<ScopedRoleGrantClause> NormalizeGrants(IReadOnlyList<ScopedRoleGrantClause> grants, string scopeType)
    {
        if (grants.Count > _options.MaxClausesPerRecord) throw new ScopedRoleValidationException("clauses.bound.exceeded", "The role has too many grant clauses.");
        return grants.Select(grant =>
        {
            var capability = _catalog.DemandCapability(grant.Capability, scopeType);
            var conditions = NormalizeConditions(grant.Conditions, capability);
            return new ScopedRoleGrantClause(capability.Key, conditions);
        }).ToList();
    }

    private List<ScopedRoleAudienceClause> NormalizeAudience(IReadOnlyList<ScopedRoleAudienceClause> audience,
        ScopedRoleCapabilityDescriptor capability)
    {
        if (audience.Count > _options.MaxClausesPerRecord) throw new ScopedRoleValidationException("clauses.bound.exceeded", "The policy has too many audience clauses.");
        return audience.Select(item =>
        {
            var value = item.Kind switch
            {
                ScopedRoleAudienceKind.Anonymous when !capability.AllowsAnonymous => throw new ScopedRoleValidationException("audience.anonymous.unsupported", "This capability does not allow an anonymous audience."),
                ScopedRoleAudienceKind.Anonymous or ScopedRoleAudienceKind.Authenticated => null,
                _ => ScopedRoleScopeRef.Require(item.Value ?? "", nameof(item.Value)),
            };
            return new ScopedRoleAudienceClause(item.Kind, value, NormalizeConditions(item.Conditions, capability));
        }).ToList();
    }

    private IReadOnlyList<ScopedRoleCondition> NormalizeConditions(IReadOnlyList<ScopedRoleCondition>? conditions,
        ScopedRoleCapabilityDescriptor capability)
    {
        var normalized = conditions?.ToArray() ?? [];
        if (normalized.Length > _options.MaxClausesPerRecord)
            throw new ScopedRoleValidationException("conditions.bound.exceeded", "A clause has more conditions than the configured evaluation bound.");
        foreach (var condition in normalized)
        {
            if (capability.Parameters is null || !capability.Parameters.Contains(condition.Parameter))
                throw new ScopedRoleValidationException("condition.parameter.unknown", $"Parameter '{condition.Parameter}' is not declared for capability '{capability.Key}'.");
            _ = ScopedRoleScopeRef.Require(condition.Value, nameof(condition.Value));
        }
        return normalized;
    }

    private static bool AudienceMatches(ScopedRoleAudienceClause audience, ScopedRoleCapabilityDescriptor capability,
        ScopedRoleActor subject, IReadOnlyList<ScopedRoleBinding> bindings,
        IReadOnlyDictionary<string, ScopedRoleDefinition> roles, ScopedRolePolicy policy,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        if (!ConditionsMatch(audience.Conditions, parameters)) return false;
        return audience.Kind switch
        {
            ScopedRoleAudienceKind.Anonymous => !subject.IsAuthenticated && capability.AllowsAnonymous,
            ScopedRoleAudienceKind.Authenticated => subject.IsAuthenticated,
            ScopedRoleAudienceKind.Subject => subject.IsAuthenticated && StringComparer.Ordinal.Equals(subject.StableSubject, audience.Value),
            ScopedRoleAudienceKind.Role => subject.IsAuthenticated && bindings.Any(x =>
                StringComparer.Ordinal.Equals(x.RoleId, audience.Value) && roles.ContainsKey(x.RoleId) &&
                x.ApprovedPolicyVersions.TryGetValue(policy.Id, out var approved) && approved == policy.Version),
            _ => false,
        };
    }

    private static bool ConditionsMatch(IReadOnlyList<ScopedRoleCondition>? conditions,
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

    private async Task<ScopedRoleDefinition> DemandRole(string id, CancellationToken ct)
        => await ScopedRoleDefinition.Get(ScopedRoleScopeRef.Require(id, nameof(id)), ct).ConfigureAwait(false)
            ?? throw new ScopedRoleValidationException("role.unknown", "The role does not exist or is unavailable.");

    private async Task<ScopedRoleBinding> DemandBinding(string id, CancellationToken ct)
        => await ScopedRoleBinding.Get(ScopedRoleScopeRef.Require(id, nameof(id)), ct).ConfigureAwait(false)
            ?? throw new ScopedRoleValidationException("binding.unknown", "The binding does not exist or is unavailable.");

    private static async Task<TEntity> Insert<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : Entity<TEntity>, IEntity<string>
    {
        using var mutation = ScopedRoleMutationGuard.Allow<TEntity>(entity.Id, Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert);
        var result = await Data<TEntity, string>.Insert(entity, ct: ct).ConfigureAwait(false);
        return result.Outcome == MutationOutcome.Inserted && result.Entity is not null
            ? result.Entity
            : throw new ScopedRoleConcurrencyException($"{typeof(TEntity).Name} already exists.");
    }

    private static async Task Replace(ScopedRoleScope entity, long expectedVersion, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleScope>(id, Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert);
        if (!await Data<ScopedRoleScope, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRoleScope changed before the guarded write committed.");
    }

    private static async Task Replace(ScopedRoleDefinition entity, long expectedVersion, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleDefinition>(id, Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert);
        if (!await Data<ScopedRoleDefinition, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRoleDefinition changed before the guarded write committed.");
    }

    private static async Task Replace(ScopedRoleBinding entity, long expectedVersion, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleBinding>(id, Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert);
        if (!await Data<ScopedRoleBinding, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRoleBinding changed before the guarded write committed.");
    }

    private static async Task Replace(ScopedRolePolicy entity, long expectedVersion, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRolePolicy>(id, Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert);
        if (!await Data<ScopedRolePolicy, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRolePolicy changed before the guarded write committed.");
    }

    private static bool GrantSetsEqual(IReadOnlyList<ScopedRoleGrantClause> left, IReadOnlyList<ScopedRoleGrantClause> right)
        => System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);
    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string>? values)
        => values is null ? new(StringComparer.Ordinal) : new(values, StringComparer.Ordinal);
    private static ScopedRoleScopeRef Normalize(ScopedRoleScopeRef scope) => scope.Normalize();
    private static bool SameScope(ScopedRoleScopeRef left, ScopedRoleScopeRef right)
        => StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) && StringComparer.Ordinal.Equals(left.Type, right.Type) && StringComparer.Ordinal.Equals(left.Id, right.Id);
    private static bool ContainsScope(IEnumerable<ScopedRoleScopeRef> ancestry, ScopedRoleScopeRef scope)
        => ancestry.Any(x => SameScope(x, scope));
    private static void DemandSameTenant(string expected, string actual)
    {
        if (!StringComparer.Ordinal.Equals(expected, actual))
            throw new ScopedRoleValidationException("tenant.mismatch", "The requested records do not belong to the same tenant.");
    }
    private DateTimeOffset Now => _clock.GetUtcNow();

    private static bool DictionaryEqual(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private sealed record RoleApproval(IReadOnlySet<string> Capabilities, Dictionary<string, long> PolicyVersions);

    private ScopedRoleActor CurrentAdministrativeActor()
    {
        var subject = _actorAccessor?.CurrentActorSubject;
        return !string.IsNullOrWhiteSpace(subject)
            ? new ScopedRoleActor(subject.Trim())
            : throw new ScopedRoleAuthorizationException("actor.unavailable", "No verified actor is bound to the current operation.");
    }

    private ScopedRoleActor CurrentSubject()
    {
        var subject = _subjectAccessor?.CurrentSubject ?? _actorAccessor?.CurrentActorSubject;
        return string.IsNullOrWhiteSpace(subject) ? ScopedRoleActor.Anonymous : new ScopedRoleActor(subject.Trim());
    }
}
