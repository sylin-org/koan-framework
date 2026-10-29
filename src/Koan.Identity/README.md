# Sylin.Koan.Identity

Koan's durable person, session, audit, erasure, and server-role core. Reference the package beside Web Auth and keep
`AddKoan()` as the only bootstrap.

## Server roles

Identity owns one `Role` collection. The role `Id` is its stable token (for example `role:member` or
`group:gardeners`); renaming the display name does not change authorization. `Members` is direct collection
membership. `Permissions` may contain application tokens; only unchanged `global:*` tokens compile into member
bags. `Metadata` is a bounded opaque string dictionary for application presentation such as purpose or color.

```csharp
var roles = services.GetRequiredService<RoleCollection>();
await roles.Define("role:member", "Member", ["global:topic_read", "global:post_create"],
    new Dictionary<string, string> { ["color"] = "#45a67f" }, ct);
await roles.Add("role:member", personId, ct);

var bag = await roles.Bag(personId, authenticated: true, ct);
var allowed = Role.CanDo(PermissionCriteria.Any("global:topic_read"), bag);
```

Every bag is in the `everyone` role; an authenticated bag is also in the `authenticated` role. When a role with
either key exists, its `global:*` permissions reach the bag like a member role's, so a permission given to everyone
is one `Define("everyone", ...)`. `bag.Roles` and `bag.Permissions` separate the two halves of `bag.Tokens`. Warm bag
reads are cache-only. Role/member/permission changes invalidate affected people (every bag for a change to
`everyone` or `authenticated`), and a generation check prevents an in-flight stale build from winning an
invalidation race. Cold membership queries and all collection sizes are bounded and fail closed.

`global:administrator` (`RoleTokens.Administrator`) is the administrator-class permission: resolved on a resource,
a bag granted it holds every permission there and no overwrite applies to it. `Role.CanDo` intersects tokens as
they are and does not expand it.

After authentication and entity lookup, applications can select criteria from the loaded resource:

```csharp
var allowed = RoleResourceAuthorization.CanDo(post, bag,
    loaded => PermissionCriteria.Any(loaded.ReadPermission));
```

## Per-resource overwrites

A `PermissionOverwrite` allows and denies `global:*` permissions for one role — `everyone` for everyone — or one
person on one resource, an id that is the application's and opaque to Identity. Resolution is Discord's: the bag's
permissions, then the `everyone` overwrite (deny, then allow), then the union of the denies and then of the allows
of the overwrites for the other roles the bag is in, then the person's own overwrite (deny, then allow).

```csharp
await roles.Overwrite("channel:workshop", OverwriteSubjectKind.Role, "role:guests",
    allow: [], deny: ["global:post_create"], ct: ct);

var bag = await roles.Bag(personId, authenticated: true, ct);           // cached
var overwrites = await roles.Overwrites("channel:workshop", ct);         // cached, every subject on the resource
var here = bag.On("channel:workshop", overwrites, DateTimeOffset.UtcNow); // pure: no I/O, no clock

if (!here.Holds("global:post_create"))
{
    var why = here.Explain("global:post_create").ContributingFacts.Single();
    // why.Kind == AccessFactKind.Deny, why.Subject == "role:guests", why.Resource == "channel:workshop"
}
```

`On` runs over any rows the caller has loaded; a row for another resource, for a role the bag is not in, for
another person, or past its `ExpiresAt` contributes nothing. `Explain` returns the explainer's `AccessDecision`:
for a missing permission, the deny of the most specific step that withheld it, or an `AccessFactKind.Ungranted`
fact when no held role grants it. `AccessExplainer.Explain(identityId, resourceId, permission)` does the same from
the cache. `AccessExplainer.RevokeAsync(fact.Ref)` revokes exactly that fact — one permission out of the
overwrite's allow or deny list, the row removed only when both are empty — so revoking an allow never lifts a deny.
The discovered overwrite contributor adds a person's overwrites to the effective-access view. Every resolution and
explanation call trims its permission and ids, and resource ids compare exactly. A bound that refuses a write throws
`RoleBoundException`, naming its configuration key.

A role subject must exist unless it is `everyone` or `authenticated`; deleting a role deletes its overwrites. An
overwrite cannot allow or deny `global:administrator`. Identity erasure removes a person's overwrites. Discord's
implicit permissions, such as a missing view hiding everything else, are the application's to apply.

Role and overwrite changes commit immediately. Inside a deferred `EntityContext.Transaction` scope they are refused
with `NotSupportedException` before anything happens; make them after `EntityContext.Commit()`, outside the scope.

Applications react through `Entity.Role.MemberAdding/Added`, `MemberRemoving/Removed`,
`PermissionsChanging/Changed`, `RoleChanging/Changed`, `RoleDeleting/Deleted`, `OverwriteChanging/Changed`, and
`OverwriteRemoving/Removed`. Before events may veto. Post events run after persistence and cache invalidation;
recursive role mutation is rejected.

## Configuration

Bounds live under `Koan:Identity:Roles`: `MaxCachedBags`, `MaxRolesPerPerson`, `MaxMembersPerRole`,
`MaxPermissionsPerRole` (also each overwrite's allow and deny), `MaxMetadataEntries`, `MaxPageSize`,
`MaxOverwritesPerResource`, `MaxOverwritesPerSubject`, and `MaxCachedResources`. A new overwrite that would exceed
its resource's or its subject's bound is refused with the key to change.

Reference `Sylin.Koan.Identity.Web` for management routes. See [TECHNICAL.md](TECHNICAL.md) and
[SEC-0021](../../docs/decisions/SEC-0021-resource-overwrites-resolve-in-discord-order.md).
