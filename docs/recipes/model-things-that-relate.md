---
type: RECIPE
recipe: model-things-that-relate
title: "Model things that belong to other things"
domain: data
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: source-verified
  scope: snippets copied from samples/fundamentals/TaskGraph, which compiles and runs
gets_you: "Parent and child Entities that read naturally one at a time, as a set, or as a stream."
works_if: "Anything in the domain belongs to something else — items to a list, posts to an author."
costs: "Nothing to operate. Unbounded child reads are the cost, which is why paging belongs here too."
ingredients:
  - "one | any entity store | Sylin.Koan.Data.Connector.Sqlite, Sylin.Koan.Data.Connector.Postgres, Sylin.Koan.Data.Connector.MySql, Sylin.Koan.Data.Connector.Mongo"
  - "one | HTTP conventions and EntityController | Sylin.Koan.Web"
  - "optional | cache a rarely-changing lookup | Sylin.Koan.Cache"
---

# Model things that belong to other things

A relationship is declared on the child, by type. The Entity vocabulary then reads the same whether you
want one, a page, or a stream.

## When this is the answer

Almost every domain: items belong to a list, a list belongs to a user, a photo belongs to an event.
Reach for it as soon as a second Entity appears.

The decisions worth making deliberately:

- **Which side owns the reference?** The child names its parent. Modelling it the other way — a parent
  holding a list of ids — is the change that makes every query awkward later.
- **Can something have two parents?** Yes, and it is common. When it does, callers must name which
  parent they mean, because the type is what disambiguates.
- **How many children can there be?** This is the question people skip, and it is the one that causes
  the outage. A parent with twenty children and a parent with two hundred thousand want different
  reads — a page, or a stream.
- **Does a lookup change rarely?** Categories, styles, statuses. Those are worth caching; the working
  data usually is not.

## Assembly

```csharp
public sealed class TodoItem : Entity<TodoItem>
{
    [Parent(typeof(Todo))]
    public string TodoId { get; set; } = "";
}

public sealed class Todo : Entity<Todo>
{
    [Parent(typeof(User))]
    public string UserId { get; set; } = "";

    [Parent(typeof(Category))]
    public string CategoryId { get; set; } = "";
}
```

Two parents on one Entity is fine; naming the type at the call site is what keeps it unambiguous.

Bound the reads at the projection rather than trusting callers:

```csharp
[Route("api/categories")]
[Pagination(Mode = PaginationMode.Required, DefaultSize = 10, MaxSize = 200)]
public sealed class CategoryController : EntityController<Category>;
```

And cache what genuinely does not move:

```csharp
[Cacheable(120)]
public sealed class Category : Entity<Category> { }
```

Depth: [read and stream Entities](../guides/data/entity-access-and-streaming.md) ·
[entity capabilities](https://github.com/sylin-org/koan-framework/blob/main/docs/guides/entity-capabilities-howto.md).

## Prove it

1. **Behavior** — create a parent and children; read one child, a page of children, and a stream of
   them, asserting the relationship holds in each.
2. **Composition** — assert paging is actually enforced. `MustPaginate` is the guard, and an unbounded
   read that quietly works is the defect.
3. **Correction** — request a child of a parent that does not exist, and a page beyond the maximum, and
   assert both fail usefully rather than returning everything.

Test with enough children that a full read would be obviously wrong. Ten rows prove nothing.

## Boundaries

- A relationship is not a foreign-key constraint. Deleting a parent does not clean up children unless
  the application says so.
- Same syntax across stores does not mean the same performance; check the adapter for what it supports.
- Paging is not authorization. A bounded read of data someone may not see is still a leak.

## Interacts with

**Tenancy.** A child must be scoped to the same tenant as its parent. Reaching a child by id without
scoping is the most common way an isolation boundary gets bypassed.

**Cache.** A cached lookup keyed without the tenant serves one customer another's categories, and every
permission check will pass.
