---
type: REF
domain: data
title: "Data adapter diagnostics and readiness"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: verified
  scope: current immutable boot provenance, runtime participation, health, and facts path
links:
  related:
    - reference/data/index
    - reference/orchestration/index
    - engineering/runtime-facts
---

# Data adapter diagnostics and readiness

## Contract

- Inputs: referenced Data adapters, provider/source election, runtime repository/direct-source use, and provider-owned
  discovery/configuration.
- Outputs: immutable startup provenance plus runtime participation, health, and facts that describe the same route
  decisions with secrets redacted.
- Error modes: an unused provider made critical, health probing a different route than Entity operations, mutable boot
  entries treated as runtime truth, or a connection opened merely because a package is referenced.
- Success criteria: operators can distinguish available, elected, participating, ready, degraded, and failed providers;
  every active probe follows the factory route used by Data.

## Availability is not participation

A direct package reference makes a provider eligible for Data election. It does not by itself make the provider a
readiness dependency. A connector becomes operationally participating when it:

- wins default provider election;
- is selected by an Entity/provider/source route; or
- serves a direct connection/source request recorded by Data diagnostics.

An available but unused provider reports non-critical `Unknown` health and must not create files, open sockets, or
initialize an external service. Once participating, it is critical and its selected physical routes are probed.

## One route decision

The adapter factory owns physical route construction. Repository creation and health use the same provider/source
resolution, including exact connection, database/bucket, credentials, and source deduplication. A repository is cached
by Data per Entity/key/provider/source route; expensive native clients are pooled by the connector at physical-source
scope.

Do not re-run provider election from an operation path to compute a storage name. Retain the already selected naming
provider and bind only the current operation's ambient partition.

## Startup provenance

A connector's domain-named `KoanModule.Report` publishes immutable configuration provenance:

- which supported key supplied a value;
- whether the value was explicit, defaulted, or resolved through discovery;
- redacted connection information;
- native storage targets and meaningful guarantee options; and
- notes that describe layered discovery or provider limitations.

Startup reporting describes composition-time knowledge. It does not mutate after boot and does not pretend every lazy
Entity route has already been used.

## Runtime truth

Data records provider/source participation when a repository or direct source is actually resolved. Health contributors
read that snapshot and probe active routes. Runtime facts project the resulting decisions through both
`/.well-known/Koan/facts` and `koan://facts`; these are two views of the same facts envelope, not separate authorities.

Keep credentials, tokens, and raw secret-bearing endpoints out of facts. Use Koan redaction/de-identification for logs,
startup provenance, health data, and corrections.

## Connector implementation

For a Data provider:

1. register the factory, options/configurator, discovery/orchestration components, client provider, and health
   contributor from one `KoanModule`;
2. derive health from `DataAdapterHealthContributorBase`;
3. expose one factory route resolver that repositories and health both use;
4. keep optional client initialization lazy unless the selected provider guarantee requires boot gating;
5. report stable configuration through `ProvenanceModuleWriter`; and
6. test available-but-unused, elected, and runtime-participating states separately.

Use standard `IOptions<T>`, `IOptionsMonitor<T>`, DI lifetimes, `IHealthContributor`, and .NET logging. Do not introduce a
parallel mutable snapshot/augmenter system when startup provenance plus runtime Data participation already express the
two different lifecycles.

## Pagination and streaming

Adapters accept explicit pagination; they do not choose defaults or caps. `Entity.All()` is a full-set request,
`Entity.Page(page, size)` supplies an exact page, and consumer boundaries such as Web own documented defaults and
safety refusals. Provider-bounded streams additionally require `DataCaps.Query.ProviderBoundedPaging` and the DATA-0107
execution contract.

## Verification checklist

- [ ] Package availability alone is non-critical and connection-free.
- [ ] Default election and runtime source use make health critical.
- [ ] Health resolves/probes the same physical route as the factory.
- [ ] Selected first use initializes once and reuses the host-owned client.
- [ ] Storage naming uses the already selected provider.
- [ ] Startup provenance is immutable and secrets are redacted.
- [ ] Runtime participation appears consistently in health and facts.
- [ ] Unpaged Entity/raw queries are not silently truncated by adapter policy.
- [ ] Focused real-backend evidence covers the provider's claimed capabilities.

## Related

- [Entity data foundation](index.md)
- [Entity access and streaming](../../guides/data/entity-access-and-streaming.md)
- [Runtime facts](../../engineering/runtime-facts.md)
- [DATA-0107 provider-bounded Entity streams](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
