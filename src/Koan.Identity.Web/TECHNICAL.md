# Koan.Identity.Web technical notes

`RoleManagementController` exposes the server-role catalog without exposing generic Entity mutation. Catalog and
member pages enforce `RoleOptions.MaxPageSize`; core collection/member/permission/metadata bounds apply again at the
mutation owner. Operator routes require `IdentityRoles.Operator`. The current-person bag route requires an
authenticated stable subject and cannot select another person.

Routes intentionally contain no scoped policy algebra, concurrency headers, or persistence row identifiers beyond
the stable role key.
