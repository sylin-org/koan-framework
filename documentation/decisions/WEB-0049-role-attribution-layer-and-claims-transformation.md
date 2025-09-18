---

id: WEB-0049
slug: role-attribution-layer-and-claims-transformation
domain: Web
status: Accepted
date: 2025-08-30
title: Role attribution layer - claims transformation, policies, and auto-registration

Context

Modern apps need a first-class, pluggable role/permission layer that cooperates with the authentication pipeline and works out-of-the-box. Controllers should be able to guard endpoints using standard [Authorize] attributes (Roles or Policy) without every app reinventing role parsing or permission mapping. Koan already provides:

- Multi-protocol authentication with provider discovery (WEB-0043, WEB-0044, WEB-0045).
- Canonical capability policy names (KoanWebPolicyNames) and a helper to bind them to roles (AuthorizationPolicyExtensions).
- Capability authorization defaults and fallbacks (WEB-0047).

The missing piece is a cohesive “role attribution layer” that:

- Extracts roles/permissions from identities consistently.
- Fuses provider-specific claims into normalized Koan roles/permissions.
- Adds roles/permissions to ClaimsPrincipal early, so [Authorize] works everywhere with zero custom code.
- Ships sane defaults with no configuration and a great DX; is extensible via KoanAutoRegistrar.

Decision

Adopt a roles/permissions attribution layer under a new package Koan.Web.Auth.Roles with these parts:

1. Contracts

- IRoleAttributionService: Computes effective roles and permissions for a ClaimsPrincipal (and optional tenant/context).
- IRoleMapContributor: Contributes default role/permission mappings from raw claims (per adapter/module). Pattern mirrors IAuthProviderContributor.
- RoleAttributionOptions: Options to control claim keys, normalization, reserved role names, and dev defaults. Bound at Koan:Web:Auth:Roles.

2. Pipeline integration

- KoanRoleClaimsTransformation: IClaimsTransformation that runs post-authentication and enriches the principal with normalized role and permission claims according to IRoleAttributionService. Adds role claims using ClaimTypes.Role and a Koan:perm claim for permissions.
- Optional DynamicAuthorizationPolicyProvider: If enabled, auto-creates policies for names like role:admin or perm:softdelete.actor, mapping to RequireRole/RequireClaim as appropriate. Disabled by default; useful for large apps that prefer convention over registration.

3. Policies and attributes

- Keep canonical policy names in KoanWebPolicyNames.
- Provide an AuthorizeByPermissionAttribute : AuthorizeAttribute helper that sets Policy = "perm:<name>" for ergonomics (optional sugar).
- Provide ServiceCollectionExtensions.AddKoanRolePolicies(Action<RolePolicyOptions>) to bind canonical capability policies to roles/permissions using a terse options object; if not called, ship a safe default mapping (see Defaults).

4. Auto-registration

- New module Koan.Web.Auth.Roles registers via Initialization/KoanAutoRegistrar.cs:
  - services.AddKoanOptions<RoleAttributionOptions>("Koan:Web:Auth:Roles");
  - services.TryAddSingleton<IRoleAttributionService, DefaultRoleAttributionService>();
  - services.TryAddEnumerable(ServiceDescriptor.Singleton<IClaimsTransformation, KoanRoleClaimsTransformation>());
  - services.AddAuthorization(o => { // optional conventional policies if RolePolicyOptions.Defaults.Enabled });
  - services.TryAddEnumerable(ServiceDescriptor.Transient<IRoleMapContributor, …>) wiring for external packages.

5. Management surface and persistence (V1)

- Provide a first-class management controller to CRUD roles, aliases, and policy bindings; persist via standard Entity<> pipeline with default models that apps can replace.
- DB is the source of truth at runtime. appsettings is a template used for initial seeding and optional re-imports.
- Entities (default, replaceable): Role, RoleAlias, RolePolicyBinding (row-versioned). First-class statics: .All/.Query/.Save/.Delete.
- Controller base route (constants):
  - GET/POST/PUT/DELETE /api/auth/roles (list/create/update/delete roles)
  - GET/POST/PUT/DELETE /api/auth/roles/aliases
  - GET/PUT/DELETE /api/auth/roles/policy-bindings/{policy}
  - POST /api/auth/roles/import?dryRun&force (seed/repopulate from appsettings template)
  - GET /api/auth/roles/export (emit current config as appsettings template)
- Caching: IRoleAttributionService uses a cached snapshot; controller writes invalidate cache; /reload endpoint is optional sugar.

6. Import/seed semantics

- On startup, if the DB has no Role/RoleAlias/RolePolicyBinding rows, seed from Koan:Web:Auth:Roles (template). Record a ConfigStamp for traceability.
- Re-population endpoint: POST /api/auth/roles/import
  - dryRun=true → return diff summary without applying.
  - force=true → replace DB state with template; otherwise merge-add new items and update matches by key.
- Production guardrails: seeding or force-reimport is disabled in Production unless explicitly allowed via either KoanEnv.AllowMagicInProduction or Roles.AllowSeedingInProduction.

7. Admin policy and initial bootstrap

- Management protection policy constant: auth.roles.admin. All admin endpoints require [Authorize(Policy = "auth.roles.admin")].
- Default binding maps auth.roles.admin → role:admin. Apps can remap via policy bindings or direct policies.
- Initial administrator bootstrap (configurable):
  - None (default in Production): no automatic admin; must be assigned explicitly via DB or import.
  - FirstUser (Development default): the first authenticated user becomes admin once; persisted, idempotent.
  - ClaimMatch: grant admin when a specific claim matches configured value(s) (e.g., email in AdminEmails[], or has provider role claim).
- All bootstrap modes are one-time elevation gates with audit; never applied silently after an admin exists.

8. Permissions catalog (V1 scope)

- V1 keeps permissions as implicit strings for zero-config DX. No catalog table is required.
- Teams that want curated descriptors can add a Permission entity and register a store; the controller will expose it when present.

9. Multi-tenancy (forward-compatible)

- Tenant scoping is supported via normalized names (e.g., tenant:{id}:role:moderator) and RoleAttributionContext. V1 does not enforce a tenant store.

Admin API (spec)

- Routes (constant base): /api/auth/roles

  - Roles
    - GET / (paged)
    - GET /{key}
    - POST /
    - PUT /{key}
    - DELETE /{key}
  - Aliases
    - GET /aliases
    - POST /aliases
    - PUT /aliases/{alias}
    - DELETE /aliases/{alias}
  - Policy bindings
    - GET /policy-bindings (paged)
    - GET /policy-bindings/{policy}
    - PUT /policy-bindings/{policy}
    - DELETE /policy-bindings/{policy}
  - Import/Export/Reload
    - POST /import?dryRun[=true]&force[=true]
    - GET /export
    - POST /reload

- Security

  - Management policy: auth.roles.admin (constant). Default: RequireRole("admin"). Apps can remap to roles or claims.
  - Bootstrap modes (one-time): None (prod default), FirstUser (dev default), ClaimMatch (e.g., AdminEmails[]).
  - 409 on RowVersion conflicts, 422 on validation errors.

- Import behavior
  - Template source: Koan:Web:Auth:Roles in appsettings (treated as seed template).
  - dryRun: returns diff only; force: replace DB state; otherwise merge-and-update.
  - Production guardrails as above.

Contract (concise)

Inputs

- ClaimsPrincipal user, optional RoleAttributionContext { TenantId?, CancellationToken }.

Outputs

- RoleAttributionResult { Roles: IReadOnlySet<string>, Permissions: IReadOnlySet<string>, Stamp: string? }.

Behavior

- Merge roles from well-known claims (roles, role, groups, Koan:role, Koan:roles).
- Merge permissions from Koan:perm, permissions, scope (both space- and claim-per-value styles).
- Normalize names to lowercase kebab, trim, dedupe, and apply aliasing (e.g., administrator → admin).
- Add ClaimTypes.Role for each role; add Koan:perm for each permission.
- If Stamp is provided, emit a Koan:rolever claim. Subsequent transformations can be no-ops if stamp unchanged.

Error modes

- Missing identity: no roles added.
- Oversized claim set: cap to configured MaxRoles/MaxPermissions; log and truncate.
- Contributor exception: swallow and log per-contributor to avoid breaking sign-in.

Defaults (no configuration)

Out-of-the-box behavior with zero app code:

- Recognize standard claim keys: roles, role, groups, Koan:role(s), permissions, scope.
- Role aliases: administrator → admin; moderator → mod; reader|viewer → reader; author|editor → author.
- Canonical roles available for immediate use: reader, author, moderator, admin.
- Canonical capability policies mapped to roles:
  - moderation.author → role:author
  - moderation.reviewer → role:moderator
  - moderation.publisher → role:admin
  - softdelete.actor → role:moderator
  - audit.actor → role:admin
- Dev safety: In Development, if no known role claims are present, fall back to reader only. No implicit admin.
- Production safety: No magic elevation. Only explicit claims produce roles. Honors KoanEnv.AllowMagicInProduction - but roles never elevate via magic.

Scope

- Applies to ASP.NET Core apps using Koan.Web.Auth. Doesn’t replace provider-specific claims; it normalizes them.
- V1 includes a DB-backed management surface and default entities, plus import/export endpoints. appsettings serves as a template for initial and optional re-seeding.

Consequences

Positive

- Controllers can use [Authorize(Roles = "admin")] or [Authorize(Policy = KoanWebPolicyNames.SoftDeleteActor)] immediately.
- Providers and modules can contribute role/permission mappings via IRoleMapContributor without tight coupling.
- Keeps existing capability policies and WEB-0047 defaults intact; this layer makes them actually plug-and-play.

Negative / Risks

- Claims bloat risk if many permissions are added per-request; mitigated by caps and opt-in permissions emission.
- Dynamic policy provider adds indirection; keep it opt-in.

Evaluation (desirability and applicability)

Positive

- Prime DX with zero-config defaults; apps get usable roles and policy bindings immediately.
- DB-backed admin surface enables safe, auditable changes without redeploying or editing appsettings.
- Import/export workflow lets teams keep intent under version control while runtime state lives in the DB.
- Admin bootstrap modes cover common needs (first user in Dev, claim-based, or explicit only) without hidden elevation.

Negative / Trade-offs

- Adds operational complexity (schema, migrations, admin API). Keep catalog optional to reduce surface in V1.
- Drift risk between template and DB; mitigated by export/import and ConfigStamp audit.
- Bootstrap needs careful guardrails to avoid accidental elevation in Production; defaults remain conservative.

Implementation notes

- Namespace: Koan.Web.Auth.Roles (new project) under src/.
- Constants: place well-known claim keys and role aliases under Infrastructure/RoleClaimConstants.cs.
- Options shape (RoleAttributionOptions):
  - ClaimKeys: Roles[], Permissions[]; EmitPermissionsClaims (bool, default true); MaxRoles (256), MaxPermissions (1024).
  - Aliases: IDictionary<string, string> (e.g., {"administrator":"admin"}).
  - DevFallbackRole: "reader"; Enabled (true).
- IRoleAttributionService default implementation composes results from IRoleMapContributor instances + raw claim extraction.
- KoanAutoRegistrar should run after auth registration; order is not critical because IClaimsTransformation executes at runtime.
- For capability policies, reuse Koan.Web.Extensions.AuthorizationPolicyExtensions to avoid duplicate logic.

Follow-ups

- Provide a small reference page in docs/reference/web-auth-roles.md with contract and examples.
- Evaluate a simple [RequirePermission] attribute and a DynamicAuthorizationPolicyProvider implementation.
- Consider tenant-aware role scoping helpers and patterns in a multi-tenant guide.

References

- WEB-0043 - Multi-protocol authentication (OIDC/OAuth2/SAML)
- WEB-0044 - Web auth discovery and health
- WEB-0045 - Auth provider adapters as separate modules
- WEB-0047 - Capability authorization - fallback and defaults
- docs/reference/web-capabilities.md
