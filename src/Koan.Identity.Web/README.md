# Sylin.Koan.Identity.Web

Reference this package to mount Identity self-service, operator, and bounded server-role APIs through `AddKoan()`.

Role routes are under `/api/identity/roles`:

- authenticated: `GET descriptor`, `GET me/bag`;
- operator: bounded role list/search/page, get, put, patch, delete;
- operator: bounded member page, add, and remove.

Role writes accept the stable key in the route and `{ name, permissions, metadata }` in the body. There is no ETag
or version binding. Every mutation goes through `RoleCollection`, so lifecycle events and bag invalidation are the
same for HTTP and headless callers. `metadata` is opaque presentation data, not authorization policy.
