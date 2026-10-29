# Sylin.Koan.Identity.Web

Reference this package to mount Identity self-service, operator, and bounded server-role APIs through `AddKoan()`.

Role routes are under `/api/identity/roles`:

- authenticated: `GET descriptor`, `GET me/bag`;
- operator: bounded role list/search/page, get, put, patch, delete;
- operator: bounded member page, add, and remove.

Role writes accept the stable key in the route and `{ name, permissions, metadata }` in the body. There is no ETag
or version binding. Every mutation goes through `RoleCollection`, so lifecycle events and bag invalidation are the
same for HTTP and headless callers. `metadata` is opaque presentation data, not authorization policy. The descriptor
names the reserved tokens, `global:administrator` among them, and every bound.

Overwrite routes are beside them under `/api/identity/overwrites`, operator only. The resource id is the query
parameter `resource`, so any id an application uses fits; the subject kind is `role` or `person`:

- `GET ?resource={id}`: the resource's overwrites, every subject;
- `PUT {role|person}/{subjectId}?resource={id}` with `{ allow, deny, expiresAt }`: set the subject's overwrite;
- `DELETE {role|person}/{subjectId}?resource={id}`: remove it.

A malformed overwrite answers 400, an unknown role 404, a bound the change would exceed 409 with the configuration
key to change, and a vetoing lifecycle handler 403. A member add beyond `MaxMembersPerRole` answers the same 409.
Only a bound (`RoleBoundException`) answers 409; any other failure keeps its own answer.

While the caller impersonates (an actor claim, SEC-0007 D8), every role write, every overwrite write and the access
console's role revoke answer 403 with the guard's answer.
