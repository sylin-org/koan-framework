---
id: DATA-0147
slug: each-source-is-minted-once-from-its-physical-store
domain: Data
status: Accepted
date: 2026-10-21
title: Each source is minted once from the store its provider resolves, and every consumer uses that plan
related:
  - DATA-0111
  - DATA-0125
  - DATA-0126
  - DATA-0143
---

# DATA-0147: Each source is minted once from the store its provider resolves, and every consumer uses that plan

## Context

A route had two `DataSourcePlan`s whose connection identities disagreed. `DataSourceRegistry.GetPlan(source, adapter)`
derived `ConnectionIdentity` from the source declaration's generic `ConnectionString`, and returned `none` when it was
blank. Core called it that way for adapter resolution, the route authority (construction and `Load`) and cutover's
`Prepare`; adapters and Direct passed the connection they had resolved. So every source whose connection came from
`ConnectionStrings:{source}`, `Koan:Data:Sources:{source}:{provider}:ConnectionString`, `Koan:Data:{provider}:ConnectionString`
or the provider's discovered default (the zero-config Default among them) was `none`, and:

- (a) all of them shared one horizon gate, so closing the Default for a cutover closed writes to unrelated sources;
- (b) a cutover between two such databases was refused as "not physically distinct";
- (c) a nested mutation from one such source into another was admitted as re-entrant, bypassing the multi-route refusal;
- (d) changing the database behind those keys was not detected at startup, contrary to DATA-0111;
- (e) Direct gates used resolved identities, so a drain on the Entity gate `none` did not block a
  `Direct(adapter: "sqlite")` write to the same file, which then landed in the old database during the copy.

Ledger NK-060. NK-178 (a route's ConnectionIdentity comes from the source definition, not the adapter's connection; an
adapter-override route such as `EntityContext.Adapter("mongo")` shared the SQLite default's gate) has the same root
cause.

## Decision

Owner decision, 2026-10-21: mint each source once, at start, from its resolved physical connection.

1. **One plan per (source, adapter).** `DataSourceRegistry` mints the plan through the elected provider's existing
   route resolution, `IDataAdapterFactory.ResolveStoreIdentity(services, source)`, which every data adapter answers
   from the route its repositories open (connection plus the settings that place data: database, schema or search
   path, bucket and scope). That identity becomes the plan's `ConnectionIdentity`, and the route identity derives from
   it. The plan is cached by (source, adapter) and is the only one: `GetPlan(source, adapter, resolvedConnection)`,
   which adapters call for their own route, returns it too.
2. **Minted at start.** `KoanDataCoreModule.Start` mints every declared source whose adapter is referenced, then
   hydrates the route authority, before any operation binds a route. A route planned later (an ambient adapter
   override) is minted on first use.
3. **Every consumer uses it.** Adapter resolution, the route authority (construction and `Load`), cutover's `Prepare`
   and its physical-distinctness check, the horizon gates and re-entrancy check, drift detection and Direct all read the
   minted plan. Direct binds the minted plan for `Direct(source:)` and `Direct(adapter:)` routes; a literal
   `WithConnectionString` is planned from its own text and is not cached, as before.
4. **The adapters without a store identity gained one**: CockroachDB, Couchbase, CouchDB, DuckDB, Firebird, MySQL,
   Redis and SQL Server now return a digest of their resolved connection and placement (Redis reads configuration only
   and opens no connection). SQLite, PostgreSQL, MongoDB, JSON and InMemory already did. A provider that answers
   `null` (vector providers, a third-party adapter) keeps the declared identity.
5. **A provider that cannot resolve a source now** (a declared source without a connection, a failed discovery) gets a
   provisional plan with the declared identity. It is not cached and is resolved again on the next request; opening the
   route still reports the real error to the caller.
6. **Stored route records are rewritten once.** On `Load`, a record that matches the minted plan loads as before. A
   record that instead matches the identities derived from the declaration (the derivation Koan used before, `none`
   for a source without a generic connection string) is accepted once and rewritten with the minted identities: the
   active route, every tracked generation, every quarantined route and the saved transition target, so a quarantine
   survives the upgrade. A record that matches neither is drift and stops the host, as before. When the source cannot
   be resolved at all, the host refuses to start with a message that says the saved route could not be verified.

## Compatibility

Behavioural, and no working application fails at boot:

- An existing `active-route.json` keeps working through the one-time rewrite. Records written by every earlier version
  carry the declared identities, so all of them take that path, including sources with a generic connection string
  (their identity moves from a digest of the connection text to the store identity). If the control directory is
  read-only, the upgraded identities are held in memory and reach the file with the next durable route change; the host
  still starts.
- Unrelated sources no longer share a gate: a cutover drain on the Default no longer refuses writes to other sources,
  and an ambient adapter override no longer shares the Default's gate.
- A cutover between two databases configured through provider-specific keys is no longer refused as not physically
  distinct.
- A nested write from one source into a different database inside a mutation is refused with the multi-route message,
  where it used to be admitted. Code that relied on that must declare a multi-route horizon.
- After this upgrade, changing the database behind `ConnectionStrings:{source}` or a provider-specific key stops the
  host with the drift message, as DATA-0111 always promised.
- `Direct(adapter:)` and `Direct(source:)` writes are blocked by a maintenance window on the same database.
- The source plan is minted at start, so a provider's route resolution (its options, including discovery) now runs at
  startup for each declared source instead of on first use. A failure there is not fatal (point 5).
- No public signature changes. Eight adapters gain an implementation of an existing optional member.

## Evidence

`SourceIdentitySpec` (Koan.Data.Cutover.Tests, SQLite and InMemory through a real `AddKoan()` host) covers each
consequence and the upgrade. All seven failed before this change and pass after:

- (a) closing the Default leaves a source configured through `Koan:Data:Sources:Archive:sqlite:ConnectionString` open;
  NK-178's adapter-override route (`EntityContext.Adapter("inmemory")`) is not closed with it;
- (b) a cutover from `ConnectionStrings:Default` to that source plans without the target-policy blocker and runs;
- (c) a nested write into it inside a Default mutation is refused as a multi-route expansion;
- (d) after a route record exists, changing `Koan:Data:Sources:Default:sqlite:ConnectionString` stops the host with the
  drift message;
- (e) during a Default drain, `Direct(adapter: "sqlite")` and `Direct(source: "Default")` writes throw
  `DataSwitchInProgressException`;
- a hand-written record with the legacy `none` identities and a quarantined legacy route loads, is rewritten with the
  minted identities and the translated quarantine, and loads again after restart.

`SourcePolicySpec` (Koan.Tests.Data.Core) covers minting once per source, the provider's own route receiving the same
plan, literal plans staying out of the cache, and a provisional plan being resolved again.
