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

Every bag contains `everyone`; authenticated bags also contain `authenticated`. Warm bag reads are cache-only.
Role/member/permission changes invalidate affected people, and a generation check prevents an in-flight stale build
from winning an invalidation race. Cold membership queries and all collection sizes are bounded and fail closed.

After authentication and entity lookup, applications can select criteria from the loaded resource:

```csharp
var allowed = RoleResourceAuthorization.CanDo(post, bag,
    loaded => PermissionCriteria.Any(loaded.ReadPermission));
```

Applications react through `Entity.Role.MemberAdding/Added`, `MemberRemoving/Removed`,
`PermissionsChanging/Changed`, `RoleChanging/Changed`, and `RoleDeleting/Deleted`. Before events may veto. Post events
run after persistence and bag invalidation; recursive role mutation is rejected.

## Configuration

Bounds live under `Koan:Identity:Roles`: `MaxCachedBags`, `MaxRolesPerPerson`, `MaxMembersPerRole`,
`MaxPermissionsPerRole`, `MaxMetadataEntries`, and `MaxPageSize`.

Reference `Sylin.Koan.Identity.Web` for management routes. See [TECHNICAL.md](TECHNICAL.md).
