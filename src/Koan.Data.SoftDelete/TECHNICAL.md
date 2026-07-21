# Sylin.Koan.Data.SoftDelete technical contract

## Responsibility

The package owns one opt-in persistent Entity semantic. `[SoftDelete]` is model metadata because it changes the
durable meaning of deletion for that Entity type. `SoftDeleteAxis` declares the complete behavior through Data's
existing axis expansion:

- managed nullable Boolean field `__deleted`;
- default read predicate `__deleted` absent or not true; and
- logical delete override `__deleted = true`.

Data Core remains unaware of soft-delete policy, and the package has no Web dependency.

## Scope and escape verbs

`SoftDeleteAmbient` carries an immutable linked stack of Entity types through `AsyncLocal`. `T.WithDeleted()` pushes
only `typeof(T)`; the axis suppresses its hide-deleted predicate only when the current stack includes the Entity type
being read. Nested same-type or cross-type scopes unwind independently.

`.HardDelete()` combines a target-scoped `OperationOverrideBypass` with the same type-targeted recycle-bin scope. The
operation bypasses only SoftDelete's delete override for that exact Entity ID; normal read filters remain in force.
`.Restore()` performs a normal save, which clears the operation-sourced managed deletion value.

## Failure and composition boundaries

- Adapters must support the managed-field filter/operation behavior supplied by Data Core.
- Atomic batch deletion rejects before the first write when soft deletion would require multiple writes.
- Other Data axes and request-contributed predicates AND-compose normally.
- No generic Web controller is supplied. Base `EntityController<T>` deletion automatically inherits this Data law;
  recycle-bin listing, restore, and purge endpoints require explicit product authorization and routes.
