# DATA-0100 post-merge finding — reaper still throws on `Job<T>`-derived entities

**Date**: 2026-06-04
**Author**: downstream consumer dogfeed
**Status**: needs decision (the open question #5 in DATA-0100 §7 of the original proposal)
**Related**: `docs/decisions/DATA-0100-comparable-encoding-contract.md`, `docs/design/DATETIMEOFFSET-CAPABILITY-PROPOSAL.md` §2.3

## TL;DR

DATA-0100 is **working at the storage boundary** — `DateTimeOffset` writes produce native `BsonDate` as designed, and a hand-written `$lt` against the new storage matches every expired-lease orphan. The **`JobOrphanReaper` query still throws** the exact same `DateTimeOffset cannot be mapped to a BsonValue` from `MongoFilterTranslator.Encode`'s fallback path. The encoding contract closed the storage hole, but a separate failure mode in the per-field class-map lookup is keeping the contract from being reached on this code path.

This is the §2.3 residual flagged in the original proposal and DATA-0100 §7 (open question 5). The answer to "expected to dissolve once SDTO lands, or worth a separate investigation pass" is: **separate pass needed**.

## Evidence

**Stack (identical to pre-DATA-0100, after Mongo 0.17.10 deployed):**

```
System.ArgumentException: .NET type System.DateTimeOffset cannot be mapped to a BsonValue.
  at MongoDB.Bson.BsonTypeMapper.MapToBsonValue(Object value)
  at Koan.Data.Connector.Mongo.MongoFilterTranslator`1.ScalarBson(...)
  at Koan.Data.Connector.Mongo.MongoFilterTranslator`1.BuildField(...)
  at Koan.Data.Connector.Mongo.MongoFilterTranslator`1.<>c__DisplayClass6_0.<Build>b__1(Filter o)
  at MongoDB.Driver.OrFilterDefinition`1..ctor(IEnumerable`1 filters)
  ...
  at Koan.Jobs.Execution.JobTypeOps`1.ReapOrphansInto(...)
```

**Storage shape confirms DATA-0100 write-path works:**

```js
db.getCollection("DownstreamConsumer.Crawling.Application.CaptureJob")
  .findOne({"leasedUntil": {$ne: null}})
// { _id: ..., status: "Running", leasedUntil: ISODate("2026-06-04T20:00:05.448Z"), ... }
// typeof leasedUntil → "Date"  (native BSON Date, not the legacy {DateTime,Ticks,Offset} document)
```

**Hand-written equivalent of the reaper's query against the new storage:**

```js
db.getCollection("DownstreamConsumer.Crawling.Application.CaptureJob").countDocuments({
    status: "Running",
    $or: [{leasedUntil: null}, {leasedUntil: {$lt: new Date()}}]
})
// → 50 (matches all expired-lease orphans correctly)
```

**The DATA-0100 registration is in the loaded DLL** — verified by decompiling the deployed nupkg (`Sylin.Koan.Data.Connector.Mongo.0.17.10`):

```csharp
TryRegister(typeof(DateTimeOffset),  new DateTimeOffsetSerializer(BsonType.DateTime));
TryRegister(typeof(DateTimeOffset?), new NullableSerializer<DateTimeOffset>(
                                       new DateTimeOffsetSerializer(BsonType.DateTime)));
TryRegister(typeof(TimeSpan),  new TimeSpanSerializer(BsonType.Int64));
TryRegister(typeof(TimeSpan?), new NullableSerializer<TimeSpan>(
                                 new TimeSpanSerializer(BsonType.Int64)));
```

So the global serializer registry has the canonical encodings. The translator just isn't reaching them on this code path.

## The narrow remaining failure

`MongoFilterTranslator.Encode` reaches the unsafe fallback path. That requires *either*:
- `ResolveScalarSerializer(field)` returned **null**, **or**
- the field serializer's `ValueType.IsInstanceOfType(value)` returned **false** (the `Nullable<T>` unwrap from `9a6e02ca` rules this out for the `DateTimeOffset?`/`DateTimeOffset` pair).

By elimination, `ResolveScalarSerializer` is returning null. That method swallows any exception inside `BsonClassMap.LookupClassMap(field.RootType)?.GetMemberMap(field.Members[0].Name)?.GetSerializer()` with a blanket `catch { return null; }`. The bug we see is consistent with that null return path being hit for `Job<T>`-derived entity types (`CrawlJob : Job<CrawlJob>` with `LeasedUntil` declared on the abstract base).

## Why DATA-0100's convergence suite didn't catch it

`TemporalConvergence` and the per-adapter spec tests use regular `Entity<T>`-derived test fixtures. The failure mode lives in the per-field class-map lookup for entities behind an **abstract generic intermediate base** (`Job<T> : Entity<T>` with the CRTP self-reference), where `LeasedUntil` is declared on the abstract base, not the concrete derived class. The convergence oracle is silent on this whole class of hierarchy.

## Two paths forward

These are alternatives, not stacked recommendations — pick one:

1. **Root-cause `ResolveScalarSerializer` returning null on Job-derived entities.** Most likely candidates:
   - `MongoOptimizationAutoRegistrar.RegisterIdentitySerializers` pre-registers a class map via `AutoMap()` + `RegisterClassMap`. If any property in the `Job<T>` hierarchy (`Duration: TimeSpan?`, `WaitForRefs: List<JobRef>`, `Metadata: Dictionary<string, object?>`, virtual `RetryPolicyDescriptor Retry`) makes AutoMap or Freeze throw, the outer `catch` logs and skips → no class map registered → `LookupClassMap` lazy-registers something different → member lookup returns null.
   - Or: an interaction between the closed-generic `Entity<TSelf>` storage-optimization detection and the abstract intermediate `Job<TSelf>` base that fails the `GetMemberMap("LeasedUntil")` step.
   - Adding a single Console.WriteLine inside the catch in `ResolveScalarSerializer` would identify which sub-cause is firing in one test run.

2. **Add a registry-based safety net in `Encode` itself.** When the field's own serializer is null, fall back to `BsonSerializer.LookupSerializer(value.GetType())` instead of `BsonValue.Create`. **Post-DATA-0100**, that registry now returns `DateTimeOffsetSerializer(BsonType.DateTime)` — so the comparand encodes to the same `BsonDate` shape as the storage, and `$lt` is a primitive date comparison. The fallback becomes correct, not just non-throwing.
   - I had this in the speculative guard I reverted before writing the proposal (commit `a17c9c89`, reverted in `2b03c9ff`). At the time I argued it masked the real bug. **Post-DATA-0100 that argument inverts**: with the canonical registry in place, the registry fallback produces the *correct* comparable encoding rather than the fragile Document shape, so it's no longer a mask — it's a layered defense for the same contract.

(1) fixes the root cause but only for this specific hierarchy pattern. (2) makes the translator structurally robust to any future class-map-lookup edge case AND aligns the fallback semantics with DATA-0100's contract. Either resolves the reaper; the two are not mutually exclusive.

## Repro hint

Easiest minimal repro is probably a unit test in `Koan.Data.Connector.Mongo.Tests`:

```csharp
public abstract class Base<TSelf> : Entity<TSelf> where TSelf : Base<TSelf>, new()
{
    public DateTimeOffset? LeasedUntil { get; set; }
}
public sealed class Derived : Base<Derived> { }

[Fact]
public void Filter_on_DateTimeOffset_member_declared_on_abstract_generic_base()
{
    var now = DateTimeOffset.UtcNow;
    var translator = new MongoFilterTranslator<Derived>(n => n);
    var compiled  = LinqFilterCompiler.Compile<Derived>(d => d.LeasedUntil < now);
    // Today this throws ArgumentException at translator.Translate(...)
    // Expected: a {leasedUntil: {$lt: <BsonDate>}} filter rendered through the DATA-0100 serializer.
    var filter = translator.Translate(compiled, typeof(Derived));
    /* assert shape */
}
```

If the test passes, the bug is something more specific to the `Job<T>` hierarchy (likely `TimeSpan? Duration` or one of the polymorphic dictionaries failing AutoMap and getting silenced). If it fails, the hierarchy itself is enough to reproduce.

## Current pipeline impact (downstream consumer)

- Pipeline IS flowing: 362 sightings → 361 captures → 16 evaluates → 2 converges. Writes work.
- 50 captures stuck `Running` with expired leases; lane gate at saturation; 294 queued behind. Reaper would clear them in one sweep if its query made it past the encoder.
- 0 packages produced (gated downstream of the lane saturation).

Once the residual is closed, the pipeline drains end-to-end.
