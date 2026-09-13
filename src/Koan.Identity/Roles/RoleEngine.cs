using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;

namespace Koan.Identity.Roles;

/// <summary>Domain-neutral owner for guarded scoped-role persistence, resolution and explanation.</summary>
public sealed class RoleEngine
{
    private readonly ScopedRoleCatalog _catalog;
    private readonly IReadOnlyList<IScopedRoleAuthorityContributor> _authorities;
    private readonly IReadOnlyList<IScopedRoleGuardContributor> _guards;
    private readonly RoleEngineOptions _options;
    private readonly TimeProvider _clock;
    private readonly ScopedRoleSnapshotCache _snapshots;
    private readonly ScopedRoleMembershipCoordinator _memberships;
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
        : this(catalog, authorities, guards, options, actorAccessor, subjectAccessor, clock, null, null) { }

    internal RoleEngine(
        ScopedRoleCatalog catalog,
        IEnumerable<IScopedRoleAuthorityContributor> authorities,
        IEnumerable<IScopedRoleGuardContributor> guards,
        IOptions<RoleEngineOptions> options,
        IIdentityActorAccessor? actorAccessor,
        IScopedRoleSubjectAccessor? subjectAccessor,
        TimeProvider? clock,
        ScopedRoleSnapshotCache? snapshots,
        ScopedRoleMembershipCoordinator? memberships = null)
    {
        _catalog = catalog;
        _authorities = authorities.ToArray();
        _guards = guards.ToArray();
        _options = options.Value;
        _actorAccessor = actorAccessor;
        _subjectAccessor = subjectAccessor;
        _clock = clock ?? TimeProvider.System;
        _snapshots = snapshots ?? new ScopedRoleSnapshotCache(_options);
        _memberships = memberships ?? new ScopedRoleMembershipCoordinator();
        if (_options.MaxAncestryDepth is < 1 or > 128) throw new InvalidOperationException("Scoped-role ancestry bound must be between 1 and 128.");
        if (_options.MaxMembersPerScope is < 1 or > 100_000) throw new InvalidOperationException("Scoped-role per-scope member bound must be between 1 and 100000.");
        if (_options.MaxPoliciesPerTenant is < 1 or > 10_000) throw new InvalidOperationException("Scoped-role policy bound must be between 1 and 10000.");
        if (_options.MaxClausesPerRecord is < 1 or > 1024) throw new InvalidOperationException("Scoped-role clause bound must be between 1 and 1024.");
        if (_options.MaxDirectoryPageSize is < 1 or > 1000) throw new InvalidOperationException("Scoped-role directory page bound must be between 1 and 1000.");
        if (_options.MaxCompiledSnapshots is < 1 or > 100_000) throw new InvalidOperationException("Scoped-role compiled snapshot bound must be between 1 and 100000.");
        if (_options.MaxDomainVersions is < 1 or > 100_000) throw new InvalidOperationException("Scoped-role domain-version bound must be between 1 and 100000.");
    }

    public Task<ScopedRoleEngineDescriptor> Describe(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new ScopedRoleEngineDescriptor(_catalog.Scopes, _catalog.Capabilities, _catalog.Resources,
            new(_options.MaxAncestryDepth, _options.MaxMembersPerScope, _options.MaxPoliciesPerTenant,
                _options.MaxClausesPerRecord)
            {
                MaxDirectoryPageSize = _options.MaxDirectoryPageSize,
                MaxCompiledSnapshots = _options.MaxCompiledSnapshots,
                MaxDomainVersions = _options.MaxDomainVersions,
            }));
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

        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.RegisterScope, target);
        var proof = await Demand(request, parentAncestry, ct, requireCommitProof: true).ConfigureAwait(false);

        var id = ScopedRoleScope.KeyFor(target);
        var current = await ScopedRoleScope.Get(id, ct).ConfigureAwait(false);
        if (current is null)
        {
            if (command.ExpectedVersion is not null)
                throw new ScopedRoleConcurrencyException("A version was supplied for a scope that does not exist.");
            var created = new ScopedRoleScope
            {
                Id = id, TenantId = target.TenantId, Type = target.Type, ScopeId = target.Id,
                OwnerSubject = actor.StableSubject,
                ParentType = parent?.Type, ParentScopeId = parent?.Id, Version = 1,
                UpdatedAt = Now, UpdatedBy = actor.StableSubject,
            };
            return await Insert(created, proof, ct).ConfigureAwait(false);
        }

        if (command.ExpectedVersion != current.Version)
            throw new ScopedRoleConcurrencyException("The scope changed after it was read.");
        current.ParentType = parent?.Type;
        current.ParentScopeId = parent?.Id;
        if (string.IsNullOrWhiteSpace(current.OwnerSubject)) current.OwnerSubject = actor.StableSubject;
        current.Version++;
        current.UpdatedAt = Now;
        current.UpdatedBy = actor.StableSubject;
        await Replace(current, command.ExpectedVersion.Value, proof, ct).ConfigureAwait(false);
        return current;
    }

    public Task<ScopedRoleDefinition> Define(DefineScopedRole command, CancellationToken ct = default)
        => Define(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRoleDefinition> Define(ScopedRoleActor actor, DefineScopedRole command, CancellationToken ct = default)
    {
        var scope = Normalize(command.Scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var grants = NormalizeGrants(command.Grants, scope.Type);
        var id = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N")
            : BoundedRequired(command.Id, nameof(command.Id), ScopedRoleInputLimits.IdentifierLength);
        var name = BoundedRequired(command.Name, nameof(command.Name), ScopedRoleInputLimits.NameLength);
        var purpose = BoundedOptional(command.Purpose, nameof(command.Purpose), ScopedRoleInputLimits.DescriptionLength);
        var presentation = NormalizePresentation(command.Presentation);
        ValidateRoleShape(id, grants);
        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.DefineRole, scope, RoleId: id,
            EffectiveCapabilities: grants.Select(x => x.Capability).ToHashSet(StringComparer.Ordinal))
        {
            EffectiveGrants = grants,
        };
        var proof = await Demand(request, ancestry, ct, requireCommitProof: true).ConfigureAwait(false);

        var role = new ScopedRoleDefinition
        {
            Id = id, TenantId = scope.TenantId, ScopeType = scope.Type, ScopeId = scope.Id,
            Name = name, Purpose = purpose,
            Grants = grants, Presentation = presentation, Version = 1, AuthorityVersion = 1,
            Status = ScopedRoleStatus.Active, UpdatedAt = Now, UpdatedBy = actor.StableSubject,
        };
        return await Insert(role, proof, ct).ConfigureAwait(false);
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
        ValidateRoleShape(role.Id, grants);
        var status = command.Status ?? role.Status;
        var name = command.Name is null ? role.Name
            : BoundedRequired(command.Name, nameof(command.Name), ScopedRoleInputLimits.NameLength);
        var purpose = command.Purpose is null ? role.Purpose
            : BoundedOptional(command.Purpose, nameof(command.Purpose), ScopedRoleInputLimits.DescriptionLength);
        var presentation = command.Presentation is null ? role.Presentation : NormalizePresentation(command.Presentation);
        var authorityChanged = status != role.Status || !GrantSetsEqual(role.Grants, grants);
        var approval = await ApprovalFor(role.Id, role.TenantId, grants, ct).ConfigureAwait(false);
        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.EditRole, scope, RoleId: role.Id,
            EffectiveCapabilities: approval.Capabilities)
        {
            EffectiveGrants = approval.Grants,
        };
        var proof = await Demand(request, ancestry, ct, requireCommitProof: true).ConfigureAwait(false);

        var priorPermissions = role.Status == ScopedRoleStatus.Active
            ? role.Grants.Select(Clone).ToArray() : [];
        var currentPermissions = status == ScopedRoleStatus.Active
            ? grants.Select(Clone).ToArray() : [];
        if (authorityChanged)
            await ScopedRoleEventRegistry.Before(ScopedRoleEventKind.PermissionsChanging,
                ChangeContext(role, null, actor, priorPermissions, currentPermissions,
                    role.Version, ScopedRoleChangePhase.Before, ct)).ConfigureAwait(false);

        role.Name = name;
        role.Purpose = purpose;
        role.Presentation = presentation;
        role.Grants = grants;
        role.Status = status;
        role.Version++;
        if (authorityChanged) role.AuthorityVersion++;
        role.UpdatedAt = Now;
        role.UpdatedBy = actor.StableSubject;
        await Replace(role, command.ExpectedVersion, proof, ct).ConfigureAwait(false);
        if (authorityChanged)
            await ScopedRoleEventRegistry.After(ScopedRoleEventKind.PermissionsChanged,
                ChangeContext(role, null, actor, priorPermissions, currentPermissions,
                    role.Version, ScopedRoleChangePhase.After, ct)).ConfigureAwait(false);
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

    public Task<bool> Add(ScopedRoleMember member, CancellationToken ct = default)
        => Add(CurrentAdministrativeActor(), member, ct);

    internal async Task<bool> Add(ScopedRoleActor actor, ScopedRoleMember member, CancellationToken ct = default)
    {
        ScopedRoleEventRegistry.DemandMutationAllowed();
        var scope = Normalize(member.Scope);
        var subject = BoundedRequired(member.Subject, nameof(member.Subject), ScopedRoleInputLimits.IdentifierLength);
        var roleId = BoundedRequired(member.RoleId, nameof(member.RoleId), ScopedRoleInputLimits.IdentifierLength);
        var key = ScopedRoleParticipant.KeyFor(scope, subject);
        return await _memberships.Run(key, async token =>
        {
            var ancestry = await LoadAncestry(scope, token).ConfigureAwait(false);
            var role = await DemandRole(roleId, token).ConfigureAwait(false);
            DemandSameTenant(scope.TenantId, role.TenantId);
            if (role.Status != ScopedRoleStatus.Active)
                throw new ScopedRoleValidationException("role.inactive", "Only an active role can receive members.");
            if (!ContainsScope(ancestry, role.Scope()))
                throw new ScopedRoleValidationException("role.scope.invalid", "The role is not defined at this scope or one of its ancestors.");

            var approval = await ApprovalFor(role.Id, role.TenantId, role.Grants, token).ConfigureAwait(false);
            var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.AddMember, scope,
                subject, role.Id, EffectiveCapabilities: approval.Capabilities)
            {
                EffectiveGrants = approval.Grants,
            };
            var proof = await Demand(request, ancestry, token, requireCommitProof: true).ConfigureAwait(false);
            var participant = await ScopedRoleParticipant.Get(key, token).ConfigureAwait(false);
            if (participant?.Contains(role.Id) == true)
            {
                await proof.Revalidate(token).ConfigureAwait(false);
                return false;
            }
            if (participant is null)
            {
                var scopeFilter = ParticipantScopeFilter(scope);
                DemandPushdown<ScopedRoleParticipant>(scopeFilter, requireProviderPaging: false);
                if (await Data<ScopedRoleParticipant, string>.Count(QueryDefinition.All.Where(scopeFilter), token)
                        .ConfigureAwait(false) >= _options.MaxMembersPerScope)
                    throw new ScopedRoleValidationException("membership.scope.limit",
                        $"The scope cannot contain more than {_options.MaxMembersPerScope} participants.");
            }

            var adding = ChangeContext(role, subject, actor, [], approval.Grants, role.Version,
                ScopedRoleChangePhase.Before, token);
            await ScopedRoleEventRegistry.Before(ScopedRoleEventKind.MemberAdding, adding).ConfigureAwait(false);
            participant ??= new ScopedRoleParticipant
            {
                Id = key,
                TenantId = scope.TenantId,
                Subject = subject,
                ScopeType = scope.Type,
                ScopeId = scope.Id,
            };
            _ = participant.Add(role.Id);
            participant.UpdatedAt = Now;
            participant.UpdatedBy = actor.StableSubject;
            await SaveMembership(participant, proof, token).ConfigureAwait(false);
            await ScopedRoleEventRegistry.After(ScopedRoleEventKind.MemberAdded,
                adding with { Phase = ScopedRoleChangePhase.After }).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public Task<bool> Remove(ScopedRoleMember member, CancellationToken ct = default)
        => Remove(CurrentAdministrativeActor(), member, ct);

    internal async Task<bool> Remove(ScopedRoleActor actor, ScopedRoleMember member, CancellationToken ct = default)
    {
        ScopedRoleEventRegistry.DemandMutationAllowed();
        var scope = Normalize(member.Scope);
        var subject = BoundedRequired(member.Subject, nameof(member.Subject), ScopedRoleInputLimits.IdentifierLength);
        var roleId = BoundedRequired(member.RoleId, nameof(member.RoleId), ScopedRoleInputLimits.IdentifierLength);
        var key = ScopedRoleParticipant.KeyFor(scope, subject);
        return await _memberships.Run(key, async token =>
        {
            var ancestry = await LoadAncestry(scope, token).ConfigureAwait(false);
            var role = await DemandRole(roleId, token).ConfigureAwait(false);
            DemandSameTenant(scope.TenantId, role.TenantId);
            if (!ContainsScope(ancestry, role.Scope()))
                throw new ScopedRoleValidationException("role.scope.inapplicable", "The role is not defined in the target scope ancestry.");
            var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.RemoveMember, scope,
                subject, role.Id);
            var proof = await Demand(request, ancestry, token, requireCommitProof: true).ConfigureAwait(false);
            var participant = await ScopedRoleParticipant.Get(key, token).ConfigureAwait(false);
            if (participant is null || !participant.Contains(role.Id))
            {
                await proof.Revalidate(token).ConfigureAwait(false);
                return false;
            }

            var approval = await ApprovalFor(role.Id, role.TenantId, role.Grants, token).ConfigureAwait(false);
            var priorPermissions = role.Status == ScopedRoleStatus.Active ? approval.Grants : [];
            var removing = ChangeContext(role, subject, actor, priorPermissions, [], role.Version,
                ScopedRoleChangePhase.Before, token);
            await ScopedRoleEventRegistry.Before(ScopedRoleEventKind.MemberRemoving, removing).ConfigureAwait(false);
            _ = participant.Remove(role.Id);
            participant.UpdatedAt = Now;
            participant.UpdatedBy = actor.StableSubject;
            var changed = participant.IsEmpty
                ? await DeleteMembership(participant, proof, token).ConfigureAwait(false)
                : await SaveMembership(participant, proof, token).ConfigureAwait(false);
            if (changed)
                await ScopedRoleEventRegistry.After(ScopedRoleEventKind.MemberRemoved,
                    removing with { Phase = ScopedRoleChangePhase.After }).ConfigureAwait(false);
            return changed;
        }, ct).ConfigureAwait(false);
    }

    public Task<ScopedRolePolicy> Replace(ReplaceScopedRolePolicy command, CancellationToken ct = default)
        => Replace(CurrentAdministrativeActor(), command, ct);

    internal async Task<ScopedRolePolicy> Replace(ScopedRoleActor actor, ReplaceScopedRolePolicy command, CancellationToken ct = default)
    {
        var scope = Normalize(command.Scope);
        var ancestry = await LoadAncestry(scope, ct).ConfigureAwait(false);
        var capabilityKey = BoundedRequired(command.Capability, nameof(command.Capability), ScopedRoleInputLimits.IdentifierLength);
        var capability = _catalog.DemandCapability(capabilityKey, scope.Type);
        var audience = NormalizeAudience(command.Audience, capability);
        var selectedRoles = audience.Where(x => x.Kind == ScopedRoleAudienceKind.Role).Select(x => x.Value!).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var roleId in selectedRoles)
        {
            if (roleId == ScopedRoleBuiltInRoles.Owner) continue;
            var role = await DemandRole(roleId, ct).ConfigureAwait(false);
            DemandSameTenant(scope.TenantId, role.TenantId);
            if (role.Status != ScopedRoleStatus.Active || !ContainsScope(ancestry, role.Scope()))
                throw new ScopedRoleValidationException("policy.role.invalid", "A replacement names an inactive or inapplicable role.");
        }
        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.ManagePolicy, scope,
            EffectiveCapabilities: new HashSet<string>([capability.Key], StringComparer.Ordinal),
            EffectiveRoleIds: selectedRoles.ToHashSet(StringComparer.Ordinal))
        {
            EffectiveAudience = audience,
        };
        var proof = await Demand(request, ancestry, ct, requireCommitProof: true).ConfigureAwait(false);

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
            }, proof, ct).ConfigureAwait(false);
        }
        if (command.ExpectedVersion != current.Version)
            throw new ScopedRoleConcurrencyException("The policy changed after it was read.");
        current.Mode = ScopedRoleOverrideMode.Replace;
        current.Audience = audience;
        current.Version++;
        current.UpdatedAt = Now;
        current.UpdatedBy = actor.StableSubject;
        await Replace(current, command.ExpectedVersion.Value, proof, ct).ConfigureAwait(false);
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
        capability = BoundedRequired(capability, nameof(capability), ScopedRoleInputLimits.IdentifierLength);
        _catalog.DemandCapability(capability, scope.Type);
        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.ResetPolicy, scope,
            EffectiveCapabilities: new HashSet<string>([capability], StringComparer.Ordinal));
        var proof = await Demand(request, ancestry, ct, requireCommitProof: true,
            usable: IsSupportedResetEnvelope,
            unsupportedCode: "authority.reset.ceiling.unsupported",
            unsupportedMessage: "A policy reset requires distinct capability-scoped reset authority without role or clause ceilings.")
            .ConfigureAwait(false);
        var policy = await ScopedRolePolicy.Get(ScopedRolePolicy.KeyFor(scope, capability), ct).ConfigureAwait(false)
            ?? throw new ScopedRoleValidationException("policy.missing", "The policy does not exist.");
        if (policy.Version != expectedVersion) throw new ScopedRoleConcurrencyException("The policy changed after it was read.");
        policy.Mode = ScopedRoleOverrideMode.Inherit;
        policy.Audience = [];
        policy.Version++;
        policy.UpdatedAt = Now;
        policy.UpdatedBy = actor.StableSubject;
        await Replace(policy, expectedVersion, proof, ct).ConfigureAwait(false);
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
        => Preview(CurrentAdministrativeActor(), new ScopedRoleActor(BoundedRequired(subject, nameof(subject), ScopedRoleInputLimits.IdentifierLength)),
            capability, target, parameters, ct);

    internal async Task<ScopedRolePreview> Preview(ScopedRoleActor actor, ScopedRoleActor subject, string capability,
        ScopedRoleScopeRef target, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        target = Normalize(target);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var request = new ScopedRoleAuthorityRequest(actor, ScopedRoleAuthorityOperation.Preview, target,
            subject.StableSubject, EffectiveCapabilities: new HashSet<string>([capability], StringComparer.Ordinal));
        _ = await DemandReadEnvelopes(request, ancestry, ct).ConfigureAwait(false);
        return new(await Plan(subject, capability, target, parameters, ct).ConfigureAwait(false));
    }

    public Task<ScopedRolePlan> Plan(string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        => Plan(CurrentSubject(), capability, target, parameters, ct);

    internal async Task<ScopedRolePlan> Plan(ScopedRoleActor subject, string capability, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        target = Normalize(target);
        capability = BoundedRequired(capability, nameof(capability), ScopedRoleInputLimits.IdentifierLength);
        ValidateParameters(parameters);
        if (subject.IsAuthenticated)
            _ = BoundedRequired(subject.StableSubject, nameof(subject), ScopedRoleInputLimits.IdentifierLength);
        var descriptor = _catalog.DemandCapability(capability, target.Type);
        var snapshotKey = new ScopedRoleSnapshotCache.CacheKey(target.TenantId, target.Type, target.Id);
        ScopedRoleSnapshotCache.CompiledSnapshot snapshot;
        try
        {
            snapshot = await _snapshots.Get(snapshotKey, token => Compile(target, token), ct).ConfigureAwait(false);
        }
        catch (ScopedRoleException) { throw; }
        catch (Exception)
        {
            throw new ScopedRoleAuthorizationException("snapshot.refresh.failed",
                "Scoped-role access could not be refreshed from its authoritative store.");
        }

        var stableSubject = subject.IsAuthenticated ? subject.StableSubject : "anonymous";
        var memberRoleIds = subject.IsAuthenticated && snapshot.SubjectRoles.TryGetValue(stableSubject, out var effectiveRoles)
            ? effectiveRoles.ToArray() : [];
        var memberships = subject.IsAuthenticated && snapshot.SubjectMemberships.TryGetValue(stableSubject, out var compiledMemberships)
            ? compiledMemberships
            : ScopedRoleMembershipSet.Compile(stableSubject, subject.IsAuthenticated, [], []);
        var reasons = new List<ScopedRoleReason>();
        bool allowed;
        ScopedRoleAudience audience;
        if (snapshot.Policies.TryGetValue(capability, out var winning))
        {
            audience = winning.Predicate;
            allowed = audience.Matches(memberships, parameters);
            reasons.Add(new(allowed ? "policy.replace.matched" : "policy.replace.denied",
                allowed ? "The nearest replacement audience matched." : "The nearest replacement audience did not match.",
                nameof(ScopedRolePolicy), winning.Id));
        }
        else
        {
            audience = snapshot.DefaultAudiences.TryGetValue(capability, out var compiledAudience)
                ? compiledAudience : ScopedRoleAudience.None;
            allowed = audience.Matches(memberships, parameters);
            reasons.Add(new(allowed ? "role.default.matched" : "role.default.denied",
                allowed ? "An applicable role grant matched." : "No applicable role grant matched."));
        }

        var versions = new Dictionary<string, long>(snapshot.ScopeVersions, StringComparer.Ordinal);
        foreach (var roleId in memberRoleIds)
            if (snapshot.RoleVersions.TryGetValue(roleId, out var version)) versions[$"role:{roleId}"] = version;
        if (winning is not null && snapshot.PolicyVersions.TryGetValue(winning.Id, out var policyVersion))
            versions[$"policy:{winning.Id}"] = policyVersion;
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
            memberRoleIds, winning?.Id, versions, Now)
        {
            Memberships = memberships,
            Audience = audience,
        };
    }

    public Task<ScopedRoleQueryPlan> Constrain<TEntity>(string action, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => Constrain<TEntity>(CurrentSubject(), action, target, parameters, ct);

    internal async Task<ScopedRoleQueryPlan> Constrain<TEntity>(ScopedRoleActor subject, string action,
        ScopedRoleScopeRef target, IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default) where TEntity : class, IEntity<string>
    {
        target = Normalize(target);
        var resource = _catalog.DemandResource<TEntity>(action, target.Type);
        var capability = resource.Actions[action];
        var plan = await Plan(subject, capability, target, parameters, ct).ConfigureAwait(false);
        if (!plan.Allowed)
            throw new ScopedRoleAuthorizationException("access.denied", "The subject is not authorized for this scoped resource operation.");
        var constraint = Filter.All(Filter.Eq(resource.TenantField!, target.TenantId),
            Filter.Eq(resource.ScopeField, target.Id));
        DemandPushdown<TEntity>(constraint, requireProviderPaging: false);
        return new(plan, constraint);
    }

    /// <summary>Run one parent-scoped, provider-filtered Entity query for the current effective subject.</summary>
    public async Task<IReadOnlyList<TEntity>> Query<TEntity>(string action, ScopedRoleScopeRef target,
        QueryDefinition? query = null, IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default) where TEntity : class, IEntity<string>
    {
        var access = await Constrain<TEntity>(action, target, parameters, ct).ConfigureAwait(false);
        var requested = (query ?? QueryDefinition.All).Where(Filter.And(query?.Filter, access.Constraint));
        DemandPushdown<TEntity>(requested.Filter!, requested.HasPagination);
        return await Data<TEntity, string>.All(requested, ct).ConfigureAwait(false);
    }

    /// <summary>Run the same constrained query with an authorization-correct count.</summary>
    public async Task<QueryResult<TEntity>> QueryWithCount<TEntity>(string action, ScopedRoleScopeRef target,
        QueryDefinition? query = null, IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default) where TEntity : class, IEntity<string>
    {
        var access = await Constrain<TEntity>(action, target, parameters, ct).ConfigureAwait(false);
        var requested = (query ?? QueryDefinition.All).Where(Filter.And(query?.Filter, access.Constraint));
        DemandPushdown<TEntity>(requested.Filter!, requested.HasPagination);
        return await Data<TEntity, string>.QueryWithCount(requested, ct).ConfigureAwait(false);
    }

    /// <summary>Load by id through the same provider-pushed scope constraint; an out-of-scope row is unavailable.</summary>
    public async Task<TEntity?> Get<TEntity>(string id, string action, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        id = ScopedRoleScopeRef.Require(id, nameof(id));
        var access = await Constrain<TEntity>(action, target, parameters, ct).ConfigureAwait(false);
        var filter = Filter.All(access.Constraint, Filter.Eq(nameof(IEntity<string>.Id), id));
        DemandPushdown<TEntity>(filter, requireProviderPaging: false);
        var rows = await Data<TEntity, string>.All(QueryDefinition.All.Where(filter), ct)
            .ConfigureAwait(false);
        return rows.Count switch
        {
            0 => null,
            1 => rows[0],
            _ => throw new InvalidOperationException("An Entity identity query returned more than one scoped row."),
        };
    }

    public async Task<long> Count<TEntity>(string action, ScopedRoleScopeRef target,
        IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
        where TEntity : class, IEntity<string>
    {
        var access = await Constrain<TEntity>(action, target, parameters, ct).ConfigureAwait(false);
        DemandPushdown<TEntity>(access.Constraint, requireProviderPaging: false);
        return await Data<TEntity, string>.Count(QueryDefinition.All.Where(access.Constraint), ct).ConfigureAwait(false);
    }

    public Task<ScopedRolePage<ScopedRoleDefinition>> Roles(ScopedRoleScopeRef target, int page = 1,
        int pageSize = 50, CancellationToken ct = default)
    {
        target = Normalize(target);
        return Directory(target, ScopedRoleAuthorityOperation.ReadDefinitions,
            Filter.All(Filter.Eq(nameof(ScopedRoleDefinition.TenantId), target.TenantId),
                Filter.Eq(nameof(ScopedRoleDefinition.ScopeType), target.Type),
                Filter.Eq(nameof(ScopedRoleDefinition.ScopeId), target.Id)),
            envelopes => RoleReadConstraint(envelopes, nameof(ScopedRoleDefinition.Id)),
            query => Data<ScopedRoleDefinition, string>.QueryWithCount(query, ct), page, pageSize, ct);
    }

    public async Task<ScopedRoleDefinition?> Role(string roleId, ScopedRoleScopeRef target,
        CancellationToken ct = default)
    {
        target = Normalize(target);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        roleId = ScopedRoleScopeRef.Require(roleId, nameof(roleId));
        _ = await DemandReadEnvelopes(new(CurrentAdministrativeActor(), ScopedRoleAuthorityOperation.ReadDefinitions,
            target, RoleId: roleId), ancestry, ct).ConfigureAwait(false);
        var filter = Filter.All(Filter.Eq(nameof(ScopedRoleDefinition.Id), roleId),
            Filter.Eq(nameof(ScopedRoleDefinition.TenantId), target.TenantId),
            Filter.Eq(nameof(ScopedRoleDefinition.ScopeType), target.Type),
            Filter.Eq(nameof(ScopedRoleDefinition.ScopeId), target.Id));
        DemandPushdown<ScopedRoleDefinition>(filter, requireProviderPaging: false);
        return (await Data<ScopedRoleDefinition, string>.All(QueryDefinition.All.Where(filter), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public Task<ScopedRolePage<ScopedRolePolicy>> Policies(ScopedRoleScopeRef target, int page = 1,
        int pageSize = 50, CancellationToken ct = default)
    {
        target = Normalize(target);
        return Directory(target, ScopedRoleAuthorityOperation.ReadPolicies,
            Filter.All(Filter.Eq(nameof(ScopedRolePolicy.TenantId), target.TenantId),
                Filter.Eq(nameof(ScopedRolePolicy.ScopeType), target.Type),
                Filter.Eq(nameof(ScopedRolePolicy.ScopeId), target.Id)),
            envelopes => CapabilityReadConstraint(envelopes, nameof(ScopedRolePolicy.Capability)),
            query => Data<ScopedRolePolicy, string>.QueryWithCount(query, ct), page, pageSize, ct);
    }

    public async Task<ScopedRolePolicy?> Policy(string capability, ScopedRoleScopeRef target,
        CancellationToken ct = default)
    {
        target = Normalize(target);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        _ = await DemandReadEnvelopes(new(CurrentAdministrativeActor(), ScopedRoleAuthorityOperation.ReadPolicies, target,
            EffectiveCapabilities: new HashSet<string>([capability], StringComparer.Ordinal)), ancestry, ct)
            .ConfigureAwait(false);
        return await ScopedRolePolicy.Get(ScopedRolePolicy.KeyFor(target, capability), ct).ConfigureAwait(false);
    }

    /// <summary>Read one participant's authorized role and group collections at one exact scope.</summary>
    public async Task<ScopedRoleMemberships> Memberships(string subject, ScopedRoleScopeRef target,
        CancellationToken ct = default)
    {
        target = Normalize(target);
        subject = BoundedRequired(subject, nameof(subject), ScopedRoleInputLimits.IdentifierLength);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var envelopes = await DemandReadEnvelopes(new(CurrentAdministrativeActor(),
            ScopedRoleAuthorityOperation.ReadMembers, target, Subject: subject), ancestry, ct).ConfigureAwait(false);
        var readableRoles = ReadableRoles(envelopes);
        var filter = Filter.All(ParticipantScopeFilter(target),
            Filter.Eq(nameof(ScopedRoleParticipant.Subject), subject));
        DemandPushdown<ScopedRoleParticipant>(filter, requireProviderPaging: false);
        var row = (await Data<ScopedRoleParticipant, string>.All(QueryDefinition.All.Where(filter), ct)
            .ConfigureAwait(false)).SingleOrDefault();
        return new(target, subject,
            Visible(row?.Roles, readableRoles), Visible(row?.Groups, readableRoles));
    }

    /// <summary>Read a provider-bounded page of subjects in one role/group collection at one exact scope.</summary>
    public async Task<ScopedRolePage<ScopedRoleMemberSubject>> Members(string roleId, ScopedRoleScopeRef target,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        target = Normalize(target);
        roleId = BoundedRequired(roleId, nameof(roleId), ScopedRoleInputLimits.IdentifierLength);
        ValidateDirectoryPage(page, pageSize);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var envelopes = await DemandReadEnvelopes(new(CurrentAdministrativeActor(),
            ScopedRoleAuthorityOperation.ReadMembers, target, RoleId: roleId), ancestry, ct).ConfigureAwait(false);
        var readableRoles = ReadableRoles(envelopes);
        if (readableRoles is not null && !readableRoles.Contains(roleId)) return new([], 0, page, pageSize);

        var collection = roleId.StartsWith("group:", StringComparison.Ordinal)
            ? nameof(ScopedRoleParticipant.Groups)
            : nameof(ScopedRoleParticipant.Roles);
        var filter = Filter.All(ParticipantScopeFilter(target),
            Filter.On(FieldPath.Of(collection), FilterOperator.Has, FilterValue.Of(roleId)));
        DemandPushdown<ScopedRoleParticipant>(filter, requireProviderPaging: true);
        var result = await Data<ScopedRoleParticipant, string>.QueryWithCount(new QueryDefinition
        {
            Page = page,
            PageSize = pageSize,
            CountStrategy = CountStrategy.Exact,
            Filter = filter,
            Sort = SortBuilder<ScopedRoleParticipant>.Build(sort => sort.OrderBy(item => item.Subject)),
        }, ct).ConfigureAwait(false);
        return new(result.Items.Select(item => new ScopedRoleMemberSubject(item.Subject)).ToArray(),
            result.TotalCount, result.Page, result.PageSize);
    }

    private async Task<ScopedRolePage<TEntity>> Directory<TEntity>(ScopedRoleScopeRef target,
        ScopedRoleAuthorityOperation operation, Filter filter,
        Func<IReadOnlyList<ScopedRoleAuthorityEnvelope>, ReadConstraint> constrain,
        Func<QueryDefinition, Task<QueryResult<TEntity>>> query, int page, int pageSize, CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        ct.ThrowIfCancellationRequested();
        target = Normalize(target);
        ValidateDirectoryPage(page, pageSize);
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var envelopes = await DemandReadEnvelopes(new(CurrentAdministrativeActor(), operation, target), ancestry, ct)
            .ConfigureAwait(false);
        var ceiling = constrain(envelopes);
        if (ceiling.Empty) return new([], 0, page, pageSize);
        if (ceiling.Filter is not null) filter = Filter.All(filter, ceiling.Filter);
        DemandPushdown<TEntity>(filter, requireProviderPaging: true);
        var result = await query(new QueryDefinition
        {
            Page = page,
            PageSize = pageSize,
            CountStrategy = CountStrategy.Exact,
            Filter = filter,
        }).ConfigureAwait(false);
        return new(result.Items, result.TotalCount, result.Page, result.PageSize);
    }

    private static ReadConstraint RoleReadConstraint(IReadOnlyList<ScopedRoleAuthorityEnvelope> envelopes, string field)
        => AggregateReadConstraint(envelopes, field, envelope => envelope.RoleIds);

    private static IReadOnlySet<string>? ReadableRoles(IReadOnlyList<ScopedRoleAuthorityEnvelope> envelopes)
        => envelopes.Any(envelope => envelope.RoleIds is null)
            ? null
            : envelopes.SelectMany(envelope => envelope.RoleIds!).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> Visible(IReadOnlyList<string>? roleIds, IReadOnlySet<string>? readableRoles)
        => (roleIds ?? []).Where(roleId => readableRoles is null || readableRoles.Contains(roleId))
            .Order(StringComparer.Ordinal).ToArray();

    private static Filter ParticipantScopeFilter(ScopedRoleScopeRef scope)
        => Filter.All(
            Filter.Eq(nameof(ScopedRoleParticipant.TenantId), scope.TenantId),
            Filter.Eq(nameof(ScopedRoleParticipant.ScopeType), scope.Type),
            Filter.Eq(nameof(ScopedRoleParticipant.ScopeId), scope.Id));

    private static ReadConstraint CapabilityReadConstraint(IReadOnlyList<ScopedRoleAuthorityEnvelope> envelopes, string field)
        => AggregateReadConstraint(envelopes, field, envelope => envelope.Capabilities);

    private static ReadConstraint AggregateReadConstraint(IReadOnlyList<ScopedRoleAuthorityEnvelope> envelopes,
        string field, Func<ScopedRoleAuthorityEnvelope, IReadOnlySet<string>?> select)
    {
        if (envelopes.Any(envelope => select(envelope) is null)) return new(null, false);
        var values = envelopes.SelectMany(envelope => select(envelope)!).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 0
            ? new(null, true)
            : new(Filter.In(field, values.Cast<object?>().ToArray()), false);
    }

    private static bool IsSupportedReadEnvelope(ScopedRoleAuthorityEnvelope envelope)
        => envelope.GrantClauses is null && envelope.AudienceClauses is null;

    private static bool IsSupportedResetEnvelope(ScopedRoleAuthorityEnvelope envelope)
        => envelope.RoleIds is null && envelope.GrantClauses is null && envelope.AudienceClauses is null;

    private void ValidateDirectoryPage(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > _options.MaxDirectoryPageSize)
            throw new ScopedRoleValidationException("directory.page.invalid",
                $"Directory pages require page >= 1 and pageSize between 1 and {_options.MaxDirectoryPageSize}.");
        _ = new QueryDefinition { Page = page, PageSize = pageSize }.EffectiveOffset();
    }

    private static void DemandPushdown<TEntity>(Filter filter, bool requireProviderPaging)
        where TEntity : class, IEntity<string>
    {
        var capabilities = Data<TEntity, string>.Capabilities;
        var support = capabilities.Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        if (FilterSplitter.Split(filter, support, typeof(TEntity)).Residual is not null)
            throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} cannot push the complete scoped-role filter to its provider.");
        if (requireProviderPaging && !capabilities.Has(DataCaps.Query.ProviderBoundedPaging))
            throw new NotSupportedException($"The adapter backing {typeof(TEntity).Name} cannot prove provider-bounded paging for this scoped-role query.");
    }

    private async Task<ScopedRoleSnapshotCache.CompiledSnapshot> Compile(ScopedRoleScopeRef target, CancellationToken ct)
    {
        var ancestry = await LoadAncestry(target, ct).ConfigureAwait(false);
        var scopeVersions = new Dictionary<string, long>(StringComparer.Ordinal);
        string? owner = null;
        foreach (var scope in ancestry)
        {
            var row = await ScopedRoleScope.Get(ScopedRoleScope.KeyFor(scope), ct).ConfigureAwait(false)
                ?? throw new ScopedRoleValidationException("scope.unknown", "The trusted target scope is not registered.");
            scopeVersions[$"scope:{row.Id}"] = row.Version;
            if (SameScope(scope, target)) owner = string.IsNullOrWhiteSpace(row.OwnerSubject) ? null : row.OwnerSubject;
        }

        var participants = new List<ScopedRoleParticipant>();
        foreach (var scope in ancestry)
        {
            var memberPage = new QueryDefinition { Page = 1, PageSize = _options.MaxMembersPerScope + 1 };
            var rows = await ScopedRoleParticipant.Query(x => x.TenantId == target.TenantId &&
                x.ScopeType == scope.Type && x.ScopeId == scope.Id, memberPage, ct).ConfigureAwait(false);
            if (rows.Count > _options.MaxMembersPerScope)
                throw new ScopedRoleValidationException("members.scope.bound.exceeded",
                    "A scope has more role participants than the configured compilation bound.");
            participants.AddRange(rows);
        }

        var roles = new Dictionary<string, ScopedRoleDefinition>(StringComparer.Ordinal);
        foreach (var roleId in participants.SelectMany(participant => participant.RoleIds)
                     .Distinct(StringComparer.Ordinal))
        {
            var role = await ScopedRoleDefinition.Get(roleId, ct).ConfigureAwait(false);
            if (role is not null && role.TenantId == target.TenantId && ContainsScope(ancestry, role.Scope()))
                roles[role.Id] = role;
        }
        var live = participants.SelectMany(participant => participant.RoleIds.Select(roleId =>
                (participant.Subject, RoleId: roleId)))
            .Where(member => roles.TryGetValue(member.RoleId, out var role) &&
                role.Status == ScopedRoleStatus.Active)
            .ToArray();

        var roleGrants = roles.Values.Where(role => role.Status == ScopedRoleStatus.Active)
            .ToDictionary(role => role.Id,
                role => (IReadOnlyList<ScopedRoleGrantClause>)role.Grants.Select(Clone).ToArray(), StringComparer.Ordinal);
        var roleMembers = live.GroupBy(member => member.RoleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlySet<string>)group.Select(member => member.Subject).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        if (owner is not null)
        {
            roleMembers[ScopedRoleBuiltInRoles.Owner] = new HashSet<string>([owner], StringComparer.Ordinal);
            roleGrants[ScopedRoleBuiltInRoles.Owner] = _catalog.Capabilities
                .Where(capability => capability.ScopeTypes.Contains(target.Type))
                .Select(capability => new ScopedRoleGrantClause(capability.Key)).ToArray();
        }
        var subjectRoles = roleMembers.SelectMany(pair => pair.Value.Select(subject => (subject, pair.Key)))
            .GroupBy(pair => pair.subject, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlySet<string>)group.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var subjectCapabilities = subjectRoles.ToDictionary(pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value.SelectMany(roleId => roleGrants.TryGetValue(roleId, out var grants)
                    ? grants.Select(grant => grant.Capability) : [])
                .ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var subjectMemberships = subjectRoles.ToDictionary(pair => pair.Key,
            pair => ScopedRoleMembershipSet.Compile(pair.Key, true, pair.Value,
                subjectCapabilities.TryGetValue(pair.Key, out var capabilities) ? capabilities : []),
            StringComparer.Ordinal);
        var defaultAudiences = roleGrants.SelectMany(pair => pair.Value.Select(grant => (RoleId: pair.Key, Grant: grant)))
            .GroupBy(item => item.Grant.Capability, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => ScopedRoleAudience.Compile(group.Select(item => new ScopedRoleAudienceClause(
                    ScopedRoleAudienceKind.Role, item.RoleId, item.Grant.Conditions)), roleMembers, false),
                StringComparer.Ordinal);

        var policyPage = new QueryDefinition { Page = 1, PageSize = _options.MaxPoliciesPerTenant + 1 };
        var allPolicies = await ScopedRolePolicy.Query(x => x.TenantId == target.TenantId, policyPage, ct).ConfigureAwait(false);
        if (allPolicies.Count > _options.MaxPoliciesPerTenant)
            throw new ScopedRoleValidationException("policies.bound.exceeded",
                "The tenant has more scoped-role policies than the configured compilation bound.");
        var scopeOrder = ancestry.Select((scope, index) => (ScopedRoleScope.KeyFor(scope), index))
            .ToDictionary(pair => pair.Item1, pair => pair.index, StringComparer.Ordinal);
        var winners = allPolicies.Where(policy => policy.Mode == ScopedRoleOverrideMode.Replace &&
                scopeOrder.ContainsKey(ScopedRoleScope.KeyFor(policy.Scope())))
            .GroupBy(policy => policy.Capability, StringComparer.Ordinal)
            .Select(group => group.OrderBy(policy => scopeOrder[ScopedRoleScope.KeyFor(policy.Scope())]).First())
            .ToArray();
        var compiledPolicies = new Dictionary<string, ScopedRoleSnapshotCache.CompiledPolicy>(StringComparer.Ordinal);
        foreach (var policy in winners)
        {
            var policyMembers = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
            foreach (var roleId in policy.Audience.Where(item => item.Kind == ScopedRoleAudienceKind.Role)
                         .Select(item => item.Value!).Distinct(StringComparer.Ordinal))
            {
                if (roleId == ScopedRoleBuiltInRoles.Owner && owner is not null)
                {
                    policyMembers[roleId] = new HashSet<string>([owner], StringComparer.Ordinal);
                    continue;
                }
                policyMembers[roleId] = roleMembers.TryGetValue(roleId, out var members)
                    ? members
                    : new HashSet<string>(StringComparer.Ordinal);
            }
            var candidates = policyMembers.Values.SelectMany(members => members)
                .Concat(policy.Audience.Where(item => item.Kind == ScopedRoleAudienceKind.Subject && item.Value is not null)
                    .Select(item => item.Value!)).ToHashSet(StringComparer.Ordinal);
            var descriptor = _catalog.DemandCapability(policy.Capability, target.Type);
            compiledPolicies[policy.Capability] = new(policy.Id, policy.Audience.Select(Clone).ToArray(),
                policyMembers, candidates, ScopedRoleAudience.Compile(policy.Audience, policyMembers,
                    descriptor.AllowsAnonymous));
        }

        return new(new(target.TenantId, target.Type, target.Id), ancestry.ToArray(), roleGrants, roleMembers,
            subjectRoles, subjectCapabilities, subjectMemberships, defaultAudiences,
            compiledPolicies, roles.Keys.ToHashSet(StringComparer.Ordinal), scopeVersions,
            roles.ToDictionary(pair => pair.Key, pair => pair.Value.Version, StringComparer.Ordinal),
            winners.ToDictionary(policy => policy.Id, policy => policy.Version, StringComparer.Ordinal));
    }

    private static ScopedRoleGrantClause Clone(ScopedRoleGrantClause clause)
        => new(clause.Capability, clause.Conditions?.ToArray());

    private static ScopedRoleAudienceClause Clone(ScopedRoleAudienceClause clause)
        => new(clause.Kind, clause.Value, clause.Conditions?.ToArray());

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

    private async Task<RoleApproval> ApprovalFor(string roleId, string tenantId,
        IReadOnlyList<ScopedRoleGrantClause> grants, CancellationToken ct)
    {
        var result = grants.Select(x => x.Capability).ToHashSet(StringComparer.Ordinal);
        var effectiveGrants = grants.ToList();
        var page = new QueryDefinition { Page = 1, PageSize = _options.MaxPoliciesPerTenant + 1 };
        var policies = await ScopedRolePolicy.Query(x => x.TenantId == tenantId, page, ct).ConfigureAwait(false);
        if (policies.Count > _options.MaxPoliciesPerTenant)
            throw new ScopedRoleValidationException("policies.bound.exceeded", "The tenant has more scoped-role policies than the configured delegation bound.");
        foreach (var policy in policies)
        {
            var roleAudiences = policy.Mode == ScopedRoleOverrideMode.Replace
                ? policy.Audience.Where(x => x.Kind == ScopedRoleAudienceKind.Role &&
                    StringComparer.Ordinal.Equals(x.Value, roleId)).ToArray()
                : [];
            if (roleAudiences.Length > 0)
            {
                result.Add(policy.Capability);
                effectiveGrants.AddRange(roleAudiences.Select(audience =>
                    new ScopedRoleGrantClause(policy.Capability, audience.Conditions)));
            }
        }
        return new(result, effectiveGrants);
    }

    private async Task<AuthorityProof> Demand(ScopedRoleAuthorityRequest request,
        IReadOnlyList<ScopedRoleScopeRef> ancestry, CancellationToken ct, bool requireCommitProof = false,
        Func<ScopedRoleAuthorityEnvelope, bool>? usable = null, string? unsupportedCode = null,
        string? unsupportedMessage = null)
    {
        if (!request.Actor.IsAuthenticated)
            throw new ScopedRoleAuthorizationException("authority.anonymous", "Authentication is required for scoped-role administration.");
        var matchedUnsupported = false;
        foreach (var contributor in _authorities)
        {
            var envelopes = await contributor.Contribute(request, ct).ConfigureAwait(false);
            foreach (var envelope in envelopes)
            {
                if (!Allows(envelope, request, ancestry)) continue;
                if (usable is not null && !usable(envelope))
                {
                    matchedUnsupported = true;
                    continue;
                }
                if (requireCommitProof && (string.IsNullOrWhiteSpace(envelope.ProofKey) || envelope.ProofVersion is null))
                    continue;
                var scopeVersions = requireCommitProof
                    ? await CaptureScopeVersions(ancestry, ct).ConfigureAwait(false)
                    : new Dictionary<string, long>(StringComparer.Ordinal);
                var boundActor = _actorAccessor?.CurrentActorSubject;
                return new AuthorityProof(envelope, async token =>
                {
                    if (!string.IsNullOrWhiteSpace(boundActor) &&
                        !StringComparer.Ordinal.Equals(_actorAccessor?.CurrentActorSubject, request.Actor.StableSubject))
                        throw new ScopedRoleAuthorizationException("actor.changed", "The verified actor changed before the mutation committed.");
                    if (!await contributor.Validate(request, envelope, token).ConfigureAwait(false))
                        throw new ScopedRoleAuthorizationException("authority.stale", "The delegation authority changed before the mutation committed.");
                    foreach (var (id, version) in scopeVersions)
                    {
                        var row = await ScopedRoleScope.Get(id, token).ConfigureAwait(false);
                        if (row is null || row.Version != version)
                            throw new ScopedRoleAuthorizationException("scope.stale", "The trusted scope ancestry changed before the mutation committed.");
                    }
                });
            }
        }
        if (matchedUnsupported && unsupportedCode is not null)
            throw new ScopedRoleAuthorizationException(unsupportedCode,
                unsupportedMessage ?? "The matching authority envelope is not supported for this operation.");
        throw new ScopedRoleAuthorizationException("authority.denied", "The actor is not authorized for this scoped-role operation.");
    }

    private async Task<IReadOnlyList<ScopedRoleAuthorityEnvelope>> DemandReadEnvelopes(
        ScopedRoleAuthorityRequest request, IReadOnlyList<ScopedRoleScopeRef> ancestry, CancellationToken ct)
    {
        if (!request.Actor.IsAuthenticated)
            throw new ScopedRoleAuthorizationException("authority.anonymous", "Authentication is required for scoped-role administration.");
        var supported = new List<ScopedRoleAuthorityEnvelope>();
        var matchedUnsupported = false;
        foreach (var contributor in _authorities)
        {
            var envelopes = await contributor.Contribute(request, ct).ConfigureAwait(false);
            foreach (var envelope in envelopes)
            {
                if (!Allows(envelope, request, ancestry)) continue;
                if (!IsSupportedReadEnvelope(envelope))
                {
                    matchedUnsupported = true;
                    continue;
                }
                supported.Add(envelope);
            }
        }
        if (supported.Count > 0) return supported;
        if (matchedUnsupported)
            throw new ScopedRoleAuthorizationException("authority.read.ceiling.unsupported",
                "Grant and audience clause ceilings cannot be projected safely onto scoped-role reads.");
        throw new ScopedRoleAuthorizationException("authority.denied",
            "The actor is not authorized for this scoped-role operation.");
    }

    private static async Task<Dictionary<string, long>> CaptureScopeVersions(
        IReadOnlyList<ScopedRoleScopeRef> ancestry, CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var scope in ancestry)
        {
            var id = ScopedRoleScope.KeyFor(scope);
            var row = await ScopedRoleScope.Get(id, ct).ConfigureAwait(false);
            if (row is not null) result[id] = row.Version;
        }
        return result;
    }

    private static bool Allows(ScopedRoleAuthorityEnvelope envelope, ScopedRoleAuthorityRequest request,
        IReadOnlyList<ScopedRoleScopeRef> ancestry)
    {
        if (!envelope.Operations.Contains(request.Operation)) return false;
        if (!SameScope(envelope.Scope, request.Target) && !(envelope.Descendants && ContainsScope(ancestry, envelope.Scope))) return false;
        if (request.RoleId is { } roleId && envelope.RoleIds is { } roleIds && !roleIds.Contains(roleId)) return false;
        if (request.EffectiveCapabilities is { Count: > 0 } capabilities && envelope.Capabilities is { } allowed && !capabilities.All(allowed.Contains)) return false;
        if (request.EffectiveRoleIds is { Count: > 0 } roles && envelope.RoleIds is { } allowedRoles && !roles.All(allowedRoles.Contains)) return false;
        if (request.EffectiveGrants is { Count: > 0 } grants && envelope.GrantClauses is { } allowedGrants &&
            !grants.All(grant => allowedGrants.Any(candidate => SameGrant(candidate, grant)))) return false;
        if (request.EffectiveAudience is { Count: > 0 } audience && envelope.AudienceClauses is { } allowedAudience &&
            !audience.All(item => allowedAudience.Any(candidate => SameAudience(candidate, item)))) return false;
        if (request.Operation == ScopedRoleAuthorityOperation.AddMember &&
            StringComparer.Ordinal.Equals(request.Actor.StableSubject, request.Subject) && !envelope.AllowSelfMembership) return false;
        return true;
    }

    private static bool SameGrant(ScopedRoleGrantClause left, ScopedRoleGrantClause right)
        => StringComparer.Ordinal.Equals(left.Capability, right.Capability) && SameConditions(left.Conditions, right.Conditions);

    private static bool SameAudience(ScopedRoleAudienceClause left, ScopedRoleAudienceClause right)
        => left.Kind == right.Kind && StringComparer.Ordinal.Equals(left.Value, right.Value) &&
            SameConditions(left.Conditions, right.Conditions);

    private static bool SameConditions(IReadOnlyList<ScopedRoleCondition>? left,
        IReadOnlyList<ScopedRoleCondition>? right)
    {
        var leftItems = left ?? [];
        var rightItems = right ?? [];
        return leftItems.Count == rightItems.Count && leftItems.Zip(rightItems).All(pair =>
            StringComparer.Ordinal.Equals(pair.First.Parameter, pair.Second.Parameter) &&
            pair.First.Operator == pair.Second.Operator &&
            StringComparer.Ordinal.Equals(pair.First.Value, pair.Second.Value));
    }

    private List<ScopedRoleGrantClause> NormalizeGrants(IReadOnlyList<ScopedRoleGrantClause> grants, string scopeType)
    {
        if (grants.Count > _options.MaxClausesPerRecord) throw new ScopedRoleValidationException("clauses.bound.exceeded", "The role has too many grant clauses.");
        return grants.Select(grant =>
        {
            var key = BoundedRequired(grant.Capability, nameof(grant.Capability), ScopedRoleInputLimits.IdentifierLength);
            var capability = _catalog.DemandCapability(key, scopeType);
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
                _ => BoundedRequired(item.Value ?? "", nameof(item.Value), ScopedRoleInputLimits.IdentifierLength),
            };
            return new ScopedRoleAudienceClause(item.Kind, value, NormalizeConditions(item.Conditions, capability));
        }).ToList();
    }

    private IReadOnlyList<ScopedRoleCondition> NormalizeConditions(IReadOnlyList<ScopedRoleCondition>? conditions,
        ScopedRoleCapabilityDescriptor capability)
    {
        var source = conditions?.ToArray() ?? [];
        if (source.Length > _options.MaxClausesPerRecord)
            throw new ScopedRoleValidationException("conditions.bound.exceeded", "A clause has more conditions than the configured evaluation bound.");
        var normalized = new ScopedRoleCondition[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var condition = source[index];
            var parameter = BoundedRequired(condition.Parameter, nameof(condition.Parameter),
                ScopedRoleInputLimits.ParameterNameLength);
            var value = BoundedRequired(condition.Value, nameof(condition.Value), ScopedRoleInputLimits.ParameterValueLength);
            if (capability.Parameters is null || !capability.Parameters.Contains(parameter))
                throw new ScopedRoleValidationException("condition.parameter.unknown", $"Parameter '{condition.Parameter}' is not declared for capability '{capability.Key}'.");
            normalized[index] = new(parameter, condition.Operator, value);
        }
        return normalized;
    }

    private async Task<ScopedRoleDefinition> DemandRole(string id, CancellationToken ct)
        => await ScopedRoleDefinition.Get(BoundedRequired(id, nameof(id), ScopedRoleInputLimits.IdentifierLength), ct).ConfigureAwait(false)
            ?? throw new ScopedRoleValidationException("role.unknown", "The role does not exist or is unavailable.");

    private async Task<TEntity> Insert<TEntity>(TEntity entity, AuthorityProof proof, CancellationToken ct)
        where TEntity : Entity<TEntity>, IEntity<string>
    {
        using var mutation = ScopedRoleMutationGuard.Allow<TEntity>(entity.Id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert, proof.Revalidate);
        var result = await Data<TEntity, string>.Insert(entity, ct: ct).ConfigureAwait(false);
        if (result.Outcome != MutationOutcome.Inserted || result.Entity is null)
            throw new ScopedRoleConcurrencyException($"{typeof(TEntity).Name} already exists.");
        Invalidate(entity);
        return result.Entity;
    }

    private async Task<bool> SaveMembership(ScopedRoleParticipant participant, AuthorityProof proof,
        CancellationToken ct)
    {
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleParticipant>(participant.Id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert, proof.Revalidate);
        await participant.Save(ct).ConfigureAwait(false);
        _snapshots.InvalidateMembership(participant.Scope());
        return true;
    }

    private async Task<bool> DeleteMembership(ScopedRoleParticipant participant, AuthorityProof proof,
        CancellationToken ct)
    {
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleParticipant>(participant.Id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Remove, proof.Revalidate);
        var removed = await participant.Remove(ct).ConfigureAwait(false);
        if (removed) _snapshots.InvalidateMembership(participant.Scope());
        return removed;
    }

    private async Task Replace(ScopedRoleScope entity, long expectedVersion, AuthorityProof proof, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleScope>(id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert, proof.Revalidate);
        if (!await Data<ScopedRoleScope, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRoleScope changed before the guarded write committed.");
        Invalidate(entity);
    }

    private async Task Replace(ScopedRoleDefinition entity, long expectedVersion, AuthorityProof proof, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRoleDefinition>(id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert, proof.Revalidate);
        if (!await Data<ScopedRoleDefinition, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRoleDefinition changed before the guarded write committed.");
        Invalidate(entity);
    }

    private async Task Replace(ScopedRolePolicy entity, long expectedVersion, AuthorityProof proof, CancellationToken ct)
    {
        var id = entity.Id;
        using var mutation = ScopedRoleMutationGuard.Allow<ScopedRolePolicy>(id,
            Koan.Data.Core.Lifecycle.EntityLifecycleOperation.Upsert, proof.Revalidate);
        if (!await Data<ScopedRolePolicy, string>.ReplaceIf(entity,
                row => row.Id == id && row.Version == expectedVersion, ct: ct).ConfigureAwait(false))
            throw new ScopedRoleConcurrencyException("ScopedRolePolicy changed before the guarded write committed.");
        Invalidate(entity);
    }

    private void Invalidate(object entity)
    {
        switch (entity)
        {
            case ScopedRoleScope scope: _snapshots.InvalidateScope(scope.Reference()); break;
            case ScopedRoleDefinition role: _snapshots.InvalidateRole(role.TenantId, role.Id); break;
            case ScopedRolePolicy policy: _snapshots.InvalidatePolicy(policy.Scope()); break;
        }
    }

    private static bool GrantSetsEqual(IReadOnlyList<ScopedRoleGrantClause> left, IReadOnlyList<ScopedRoleGrantClause> right)
        => System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);
    private static void ValidateRoleShape(string roleId, IReadOnlyList<ScopedRoleGrantClause> grants)
    {
        if (roleId.StartsWith("group:", StringComparison.Ordinal) && grants.Count > 0)
            throw new ScopedRoleValidationException("group.capabilities.unsupported",
                "group:* identities are audience memberships and cannot grant capabilities.");
    }
    private ScopedRoleChangeContext ChangeContext(ScopedRoleDefinition role, string? subject, ScopedRoleActor actor,
        IReadOnlyList<ScopedRoleGrantClause> previous, IReadOnlyList<ScopedRoleGrantClause> current,
        long version, ScopedRoleChangePhase phase, CancellationToken ct)
        => new(role.TenantId, role.Scope(), role.Id, subject, actor, previous.Select(Clone).ToArray(),
            current.Select(Clone).ToArray(), Now, version, phase, ct);
    private static Dictionary<string, string> NormalizePresentation(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null) return new(StringComparer.Ordinal);
        if (values.Count > ScopedRoleInputLimits.PresentationEntries)
            throw new ArgumentException($"Presentation cannot contain more than {ScopedRoleInputLimits.PresentationEntries} entries.", nameof(values));
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            var key = BoundedRequired(pair.Key, "presentation key", ScopedRoleInputLimits.PresentationKeyLength);
            var value = pair.Value ?? "";
            if (value.Length > ScopedRoleInputLimits.PresentationValueLength)
                throw new ArgumentException($"presentation value cannot exceed {ScopedRoleInputLimits.PresentationValueLength} characters.", nameof(values));
            if (!normalized.TryAdd(key, value)) throw new ArgumentException("Presentation keys must be unique after normalization.", nameof(values));
        }
        return normalized;
    }

    private static void ValidateParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null) return;
        if (parameters.Count > ScopedRoleInputLimits.Parameters)
            throw new ArgumentException($"No more than {ScopedRoleInputLimits.Parameters} access parameters are supported.", nameof(parameters));
        foreach (var pair in parameters)
        {
            _ = BoundedRequired(pair.Key, "parameter name", ScopedRoleInputLimits.ParameterNameLength);
            if (pair.Value is null) continue;
            if (pair.Value is System.Text.Json.JsonElement json)
            {
                if (json.ValueKind is System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object ||
                    json.GetRawText().Length > ScopedRoleInputLimits.ParameterValueLength)
                    throw new ArgumentException("Access parameters must be bounded scalar values.", nameof(parameters));
                continue;
            }
            var type = pair.Value.GetType();
            var scalar = type.IsPrimitive || type.IsEnum || pair.Value is string or decimal or Guid or
                DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan;
            if (!scalar || Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)?.Length >
                ScopedRoleInputLimits.ParameterValueLength)
                throw new ArgumentException("Access parameters must be bounded scalar values.", nameof(parameters));
        }
    }

    private static string BoundedRequired(string value, string name, int maxLength)
    {
        var normalized = ScopedRoleScopeRef.Require(value, name);
        return normalized.Length <= maxLength ? normalized
            : throw new ArgumentException($"{name} cannot exceed {maxLength} characters.", name);
    }

    private static string? BoundedOptional(string? value, string name, int maxLength)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized
            : throw new ArgumentException($"{name} cannot exceed {maxLength} characters.", name);
    }

    private static ScopedRoleScopeRef Normalize(ScopedRoleScopeRef scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new(
            BoundedRequired(scope.TenantId, nameof(scope.TenantId), ScopedRoleInputLimits.IdentifierLength),
            BoundedRequired(scope.Type, nameof(scope.Type), ScopedRoleInputLimits.IdentifierLength),
            BoundedRequired(scope.Id, nameof(scope.Id), ScopedRoleInputLimits.IdentifierLength));
    }
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

    private sealed record RoleApproval(IReadOnlySet<string> Capabilities,
        IReadOnlyList<ScopedRoleGrantClause> Grants);
    private sealed record AuthorityProof(ScopedRoleAuthorityEnvelope Envelope,
        Func<CancellationToken, ValueTask> Revalidate);
    private sealed record ReadConstraint(Filter? Filter, bool Empty);

    private ScopedRoleActor CurrentAdministrativeActor()
    {
        var subject = _actorAccessor?.CurrentActorSubject;
        return !string.IsNullOrWhiteSpace(subject)
            ? new ScopedRoleActor(BoundedRequired(subject, nameof(subject), ScopedRoleInputLimits.IdentifierLength))
            : throw new ScopedRoleAuthorizationException("actor.unavailable", "No verified actor is bound to the current operation.");
    }

    private ScopedRoleActor CurrentSubject()
    {
        var subject = _subjectAccessor?.CurrentSubject ?? _actorAccessor?.CurrentActorSubject;
        return string.IsNullOrWhiteSpace(subject) ? ScopedRoleActor.Anonymous
            : new ScopedRoleActor(BoundedRequired(subject, nameof(subject), ScopedRoleInputLimits.IdentifierLength));
    }
}
