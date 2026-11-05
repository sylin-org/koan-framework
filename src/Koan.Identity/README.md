# Sylin.Koan.Identity

Koan's durable person, session, audit, erasure, and server-role core. Reference the package beside Web Auth and keep
`AddKoan()` as the only bootstrap.

## A person and their logins

A person's id is Koan's own, a GUID v7 made the first time one of their logins signs in; it is never a provider's subject.
Each login (`ExternalIdentityLink`) is bound to one person, who holds at most one per provider and never fewer than one.
A signed-in person binds another through its provider's challenge at `/me/connections/{provider}/link`, which takes the
challenge's `return` and `failure`; a new login of a provider takes the place of the one they held, and a login that is
someone else's is refused (`login_taken`). They unbind one with `DELETE /api/identity/me/connected/{linkId}`
(Koan.Identity.Web), never their last. `Koan:Web:Auth:ReConsent:ForceOnLink` asks for a recent sign-in before binding
(`sign_in_first`). See SEC-0040.

## What is taken from a person reaches what they hold

A request's roles are the person's now. The cookie carries none: each request projects them from the person's compiled
bag, so a role removed stops authorizing their next request, a role granted starts, and nobody signs in again.
`IdentityLifecycleService.SuspendAsync`, deactivation and erasure close the person's agents too: their refresh is
refused at once, and their client grants are removed, so reactivating them brings no agent back.

Koan.Identity reports each person's standing to the authorization server: a live person's next refresh carries their
current roles and name, and a suspended, deactivated or erased person's mints nothing. An application adds what it knows
of a person by implementing `ISubjectStandingContributor`; see Web Auth's README.

## Server roles

Identity owns one `Role` collection. The role `Key` is its stable token (for example `role:member` or
`group:gardeners`), and its row id is composed from it (`Role.KeyFor`); renaming the display name does not change
authorization. A role's members are `RoleMember` rows,
one per person, so a role holds any number of them. `Permissions` may contain application tokens; only unchanged
`global:*` tokens compile into member bags. `Metadata` is a bounded opaque string dictionary for application
presentation such as purpose or color.

```csharp
var roles = services.GetRequiredService<RoleCollection>();
await roles.Define("role:member", "Member", ["global:topic_read", "global:post_create"],
    new Dictionary<string, string> { ["color"] = "#45a67f" }, ct);
await roles.Add("role:member", personId, ct);                   // one row; adding again changes nothing

var bag = await roles.Bag(personId, authenticated: true, ct);
var allowed = Role.CanDo(PermissionCriteria.Any("global:topic_read"), bag);
var isMember = bag.InRole("role:member");                        // or: await RoleMember.Get(RoleMember.KeyFor(key, personId))

var page = await roles.Members("role:member", after: null, take: 50, ct);   // in person order
var next = page.Next is null ? null : await roles.Members("role:member", page.Next, 50, ct);
var held = await roles.ForPerson(personId, ct);                  // the roles one person holds
```

Every bag is in the `everyone` role; an authenticated bag is also in the `authenticated` role. When a role with
either key exists, its `global:*` permissions reach the bag like a member role's, so a permission given to everyone
is one `Define("everyone", ...)`. `bag.Roles` and `bag.Permissions` separate the two halves of `bag.Tokens`. Warm bag
reads are cache-only; a bag compiles from one read of the person's memberships and one read of those roles. Adding
or removing a member compiles that person's bag again; defining, moving or deleting a role, or changing its
permissions, compiles every bag again, without reading the role's members; a rename or a metadata change alters no
bag. A generation check prevents
an in-flight stale build from winning an invalidation race.

### The order and who manages what

Every role stands in one order, as in Discord: `Role.Position` 0 is `everyone`, and every other role holds one of 1
to the number of them, a higher position above a lower. `Define` places a new role at the top, or at `position:` —
the role there and those above it move up one — and `Move` moves one; deleting a role moves the roles above it down
one. A position past either end lands on that end. A new `authenticated` role is placed at 1, since every signed-in
person holds it. `Page` reads the order a page at a time, the highest first.

```csharp
var bag = await roles.Bag(actorId, authenticated: true, ct);   // bag.Highest: the position of their highest role
await roles.Define("role:helper", "Helper", ["global:post_create"], position: bag.Highest, ct: ct); // directly below it
if (await roles.Manages(bag, "role:helper", to: 2, ct)) await roles.Move("role:helper", 2, ct);
var page = await roles.Page(pageSize: 50, ct: ct);
var editable = page.Items.Where(role => bag.Manages(role));     // pure, over the rows read
```

An actor manages a role below their highest, and that role's members, and moves it only to a position below their
highest; a bag whose roles grant `global:administrator` manages every role and moves it anywhere; nobody manages their
own highest role, and nobody moves `everyone`. `Manages` answers the hierarchy; whether the actor may manage roles at
all is the application's own permission, checked beside it. Of two people, the one whose bag has the higher `Highest`
stands above the other.

A person holds at most `MaxRolesPerPerson` roles: adding them to one more is refused with `RoleBoundException`,
naming its configuration key. Nothing bounds how many members a role holds. A key — a role key, a person id, a
resource id, a permission — holds no control character and no unpaired surrogate. Reads compare keys exactly, whatever the store's collation,
and a listing's `Next` is an opaque cursor: hand it back as `after`. A page can hold fewer rows than `take` while a
`Next` follows.

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

var bag = await roles.Bag(personId, authenticated: true, ct);                   // cached
var overwrites = await roles.OverwritesFor(bag, "channel:workshop", ct);        // cached: the rows this bag meets there
var here = bag.On("channel:workshop", overwrites, DateTimeOffset.UtcNow);       // pure: no I/O, no clock

if (!here.Holds("global:post_create"))
{
    var why = here.Explain("global:post_create").ContributingFacts.Single();
    // why.Kind == AccessFactKind.Deny, why.Subject == "role:guests", why.Resource == "channel:workshop"
}
```

`OverwritesFor` reads only what the bag can meet on the resource: the resource's overwrites for the roles the bag is
in, compiled once per resource, and the person's own, read by key and cached. `On` gives the same answer over those
rows as over every overwrite on the resource. `On` runs over any rows the caller has loaded; a row for another
resource, for a role the bag is not in, for another person, or past its `ExpiresAt` contributes nothing. `Explain`
returns the explainer's `AccessDecision`: for a missing permission, the deny of the most specific step that withheld
it, or an `AccessFactKind.Ungranted` fact when no held role grants it. `AccessExplainer.Explain(identityId, resourceId,
permission)` does the same from the cache, and `AccessExplainer.WhyAsync(identityId, resourceId)` lists the overwrite
facts on that resource beside the identity-wide ones. `AccessExplainer.RevokeAsync(fact.Ref)` revokes exactly that
fact — one permission out of the overwrite's allow or deny list, the row removed only when both are empty — so
revoking an allow never lifts a deny. Every resolution and explanation call trims its permission and ids, and
resource ids compare exactly.

For administration, `Overwrites(resourceId, after, take)` pages a resource's overwrites, roles first by key and then
people by id, and `OverwritesOf(subjectKind, subjectId, after, take)` pages one subject's across resources in resource
order. Nothing bounds how many overwrites a resource or a subject holds.

A role subject must exist unless it is `everyone` or `authenticated`; deleting a role deletes its members and its
overwrites. It first takes away everything the role grants — its permissions and its overwrites' allows — so on a route
with no holds a deletion that stops part way leaves members holding only the role's denies; on a route that offers holds it
leaves the role as it was. An overwrite cannot allow or deny `global:administrator`. Identity erasure removes a person's memberships
and overwrites. Discord's implicit permissions, such as a missing view hiding everything else, are the application's
to apply.

Inside `EntityContext.Hold` a role, member or overwrite change joins the hold, so a command decides, changes a role and writes
its own rows as one outcome:

```csharp
using (await EntityContext.Hold("give-role", ct))
{
    if ((await roles.Bag(actor, true, ct)).Manages((await roles.Get(key, ct))!))   // sees this hold's own changes
    {
        await roles.Add(key, person, ct);
        await new RoleGiven { Person = person, Role = key }.Save(ct);
        await EntityContext.Commit(ct);
    }
}
```

A rollback or a failed commit leaves no row, no moved position and no changed bag, and raises no after-event. Other flows
read what is committed until the commit, and their role changes wait for the hold to end. A change that throws part way leaves
what it wrote in the hold, so let the exception end the scope.

Outside any scope, on a route that offers holds, a change is a short hold of its own: one outcome, in its turn behind the holds
ahead of it. On a route that does not, it commits as it goes. Inside a deferred `EntityContext.Transaction` scope it is refused
with `NotSupportedException` before anything happens, because such a scope cannot decide from what it reads.

Applications react through `Entity.Role.MemberAdding/Added`, `MemberRemoving/Removed`,
`PermissionsChanging/Changed`, `RoleChanging/Changed`, `RoleDeleting/Deleted`, `OverwriteChanging/Changed`, and
`OverwriteRemoving/Removed`. Before events may veto; in a hold what they write joins it, so they stay short and on its route.
Post events run after persistence and cache invalidation: in a hold, after its commit and outside it, where a failure is logged
because the commit stands; on a route with no holds, a failure reaches the caller as `RolePostEventException`. Recursive role
mutation is rejected.

## Configuration

Settings live under `Koan:Identity:Roles`. The bounds a person can meet are properties of the model:
`MaxRolesPerPerson`, `MaxPermissionsPerRole` (also each overwrite's allow and deny), and `MaxMetadataEntries`.
`MaxPageSize` bounds one page of a listing. The rest size memory and refuse nothing: `MaxCachedBags` (people),
`MaxCachedResources` (resources whose role overwrites stay compiled) and `MaxCachedPersonOverwrites` (a person's own
overwrite on a resource, or its absence). Past a capacity the cache evicts an entry nothing has read lately and keeps
the rest warm.

Reference `Sylin.Koan.Identity.Web` for management routes. See [TECHNICAL.md](TECHNICAL.md),
[SEC-0021](../../docs/decisions/SEC-0021-resource-overwrites-resolve-in-discord-order.md),
[SEC-0030](../../docs/decisions/SEC-0030-bounds-belong-to-reads.md),
[SEC-0031](../../docs/decisions/SEC-0031-roles-stand-in-one-order.md) and
[SEC-0036](../../docs/decisions/SEC-0036-a-role-change-joins-a-hold.md).
