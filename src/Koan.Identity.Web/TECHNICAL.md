# Koan.Identity.Web technical notes

`RoleManagementController` exposes the server-role catalog without exposing generic Entity mutation. Catalog pages
and member pages enforce `RoleOptions.MaxPageSize`; a member page is `RoleCollection.Members`, one read by role then
person continued from the cursor the previous page handed back, and the core permission, metadata and roles-per-person bounds apply
at the mutation owner. Operator routes require `IdentityRoles.Operator`. The current-person bag route requires an
authenticated stable subject and cannot select another person.

`PermissionOverwriteController` exposes overwrites the same way: every write goes through `RoleCollection`, and the
per-list bound applies there. Its listing is `RoleCollection.Overwrites`, a page at a time. The resource id is opaque,
so it travels in the query rather than the path. Both controllers share `RoleManagementExceptionFilter` (400 for a
malformed argument or cursor, 403 for a veto); the member add maps `RoleBoundException`, and nothing else, to 409
itself. Every write checks `ImpersonationGuard` first — `role.manage` for role writes, `overwrite.manage` for overwrite
writes — before it reaches `RoleCollection`.

Routes intentionally contain no scoped policy algebra, concurrency headers, or persistence row identifiers beyond
the stable role key, the subject, the resource id and a page's cursor.
