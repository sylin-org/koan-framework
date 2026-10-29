# Koan.Identity.Web technical notes

`RoleManagementController` exposes the server-role catalog without exposing generic Entity mutation. Catalog and
member pages enforce `RoleOptions.MaxPageSize`; core collection/member/permission/metadata bounds apply again at the
mutation owner. Operator routes require `IdentityRoles.Operator`. The current-person bag route requires an
authenticated stable subject and cannot select another person.

`PermissionOverwriteController` exposes overwrites the same way: every write goes through `RoleCollection`, and the
per-resource, per-subject and per-list bounds apply there. The resource id is opaque, so it travels in the query
rather than the path. Both controllers share `RoleManagementExceptionFilter` (400 for a malformed argument, 403 for
a veto); the overwrite write and the member add map `RoleBoundException`, and nothing else, to 409 themselves. Every write checks
`ImpersonationGuard` first — `role.manage` for role writes, `overwrite.manage` for overwrite writes — before it
reaches `RoleCollection`.

Routes intentionally contain no scoped policy algebra, concurrency headers, or persistence row identifiers beyond
the stable role key, the subject and the resource id.
