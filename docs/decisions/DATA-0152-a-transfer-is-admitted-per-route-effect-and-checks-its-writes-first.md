---
id: DATA-0152
slug: a-transfer-is-admitted-per-route-effect-and-checks-its-writes-first
domain: Data
status: Accepted
date: 2026-10-21
title: A transfer is admitted per route effect and checks every side it writes before it reads
related:
  - DATA-0079
  - DATA-0111
  - DATA-0125
  - DATA-0143
  - DATA-0149
---

# DATA-0152: A transfer is admitted per route effect and checks every side it writes before it reads

Amends [DATA-0079](DATA-0079-entity-transfer-helpers.md), which defines the transfer builders.

## Context

A transfer resolves both endpoints and enters one operation horizon over both routes before its first read
(DATA-0125). Two things were wrong with that admission:

- **Every route was admitted as a mutation.** A `Copy` only reads its origin and a `Mirror(Push)` only reads its
  `From()`, yet both counted their origin as an active mutation. While a cutover had closed the default's writes, a
  `Copy` out of the default failed with `DataSwitchInProgressException`, and a running `Copy` held the default's drain
  open (NK-253).
- **What a write would refuse was discovered at the first write.** Source policy, the tenant gate and the adapter's
  isolation capability are enforced per facade call. A `Move` from a `ReadOnly` source copied its whole selection into
  the destination and only then failed at the first delete; a `Copy` to a read-only or non-isolating destination read
  a source page before it failed (NK-250).

## Decision

1. **Each route is admitted with its effect.** `DataOperationHorizon.EnterMany` takes each route with whether the
   operation writes it. A written route is counted as a mutation and refused while its writes are closed; a route only
   read is refused only while its reads are closed, is never counted, and never delays a drain. Two endpoints on one
   connection are one route, written if either side writes. Inside the horizon only the written routes accept writes.
2. **What a transfer writes:**

   | Transfer | `From()` | `To()` |
   |---|---|---|
   | `Copy` | read | written |
   | `Move` | written (deleted) | written |
   | `Mirror(Push)` | read | written |
   | `Mirror(Pull)` | written | read |
   | `Mirror(Bidirectional)` | written | written |

3. **Every written side is checked before the first read.** Admission asks the routed repository of each written side
   to refuse now whatever its first write would refuse: source policy (`Access`), the bound segmentation, the
   registered storage guards (the tenant gate), and, under an active scope, the adapter's isolation capability. The
   check performs no I/O. A refusal throws the same exception the first write would have thrown, before anything is
   read or written.
4. The same-place check (DATA-0125) and the Move proof (DATA-0143) still run first, so a no-op `Copy` onto its own
   container is skipped without admission and needs no open route.

## Consequences

- A `Copy`, a `Mirror(Push)` and a `Mirror(Pull)` keep running against a side whose writes a cutover has closed, as long
  as they only read it. A `Move` and a bidirectional `Mirror` are refused there, as before.
- A transfer that cannot write a side fails before it does any work, with the error its first write would have raised.

## Evidence

`EntityTransferDslSpec` (Data.Core): `Copy_FromARouteWhoseWritesAreClosed_RunsAndMoveIsRefused` (NK-253) and
`Transfer_ThatWritesAReadOnlySource_IsRefusedBeforeItReadsOrWrites` (NK-250) fail before this change and pass after it.
