# Koan Core Contracts (draft)

This mirrors and refines the core contracts with your guidance.

## Data
- IEntity<TKey>, IHasVersion
- IDataRepository<TEntity, TKey>
  - GetAsync, QueryAsync, UpsertAsync, DeleteAsync
  - UpsertManyAsync, DeleteManyAsync
  - CreateBatch() → IBatchSet<TEntity, TKey>
  - Optional: IQueryCapabilities (String, Linq)
  - Optional: IWriteCapabilities (BulkUpsert, BulkDelete, AtomicBatch)
  - Optional markers: IBulkUpsert<TKey>, IBulkDelete<TKey>
- IBatchSet<TEntity, TKey>
  - Add/Update/Delete/Clear → SaveAsync(BatchOptions, ct)
- Repo operation pipeline
  - IRepoBehavior<T>, OperationType, RepoOperationContext<T>, RepoOperationOutcome
  - Ordering by priority and constraints: Before(X), After(Y)

## Messaging
- IMessageBus
  - SendAsync(object message, CancellationToken)
  - SendManyAsync(IEnumerable<object> messages, CancellationToken)
- IMessageHandler<T>
  - HandleAsync(MessageEnvelope envelope, T message, CancellationToken)
- MessageEnvelope
  - Id, TypeAlias, CorrelationId, CausationId, Headers, Attempt, Timestamp
- ITypeAliasRegistry
  - GetAlias(Type), Resolve(string alias)
- IMessagingCapabilities (per provider)
  - DelayedDelivery, DeadLettering, Transactions, MaxMessageSizeKB, MessageOrdering, ScheduledEnqueue, PublisherConfirms
- Negotiation and Effective plan
  - A negotiation step computes an EffectiveMessagingPlan per bus, reconciling requested features (delay/DLQ/retry) with provider capabilities.

Notes
- Attributes provide metadata and routing hints: [Message(Alias, Version, Bus, Group)], [PartitionKey], [Header(name)], [Sensitive], [DelaySeconds], [IdempotencyKey].
  - [Header] values are promoted to transport headers by providers that support it.
  - [PartitionKey] may influence routing/ordering (partition suffixing or partitions depending on provider).
  - Aliases can optionally include a version suffix (e.g., `Alias@v1`) when `Koan:Messaging:IncludeVersionInAlias = true`.
- Providers can provision topology on startup based on options (guarded in Production by a global switch).
- See ADR-0021 (messaging capabilities and negotiation) and ADR-0022 (provisioning defaults, aliases/attributes, dispatcher).

## Webhooks
- IWebhookVerifier/IWebhookSender; delivery policies and DLQ.

## AI
- IAgentRuntime, IAgentTool, IVectorStore, ILLMClient.

## Config/precedence
- Explicit config precedence, opt-in discovery in dev, fail-fast on explicit config errors.

## Facade helpers (Data<TEntity, TKey>)
- GetAsync, All, Query (string), DeleteAsync
- UpsertManyAsync, DeleteManyAsync
- Batch() → IBatchSet<TEntity, TKey>
  - IEnumerable<TEntity>.AsBatch() helper builds a pre-filled batch; IBatchSet.AddRange adds many
- QueryCaps (IQueryCapabilities) and WriteCaps (IWriteCapabilities) for optional capability inspection

## Notes
- Versioning (snapshots) is deferred to a later milestone; not part of the initial built-ins.
 - Naming rationale: We unify on IEntity<TKey> for simplicity; "aggregate root" remains a doc concept for ownership and consistency boundaries.
 - Configuration: Koan ensures an IConfiguration is available when using the one-liner `services.StartKoan()` (console/non-host scenarios). If an IConfiguration is already registered by the host, Koan does not override it. Adapters bind options from configuration sections (e.g., `Koan:Data:...`).

## Health & criticality contracts
 - Pull checks: `IHealthContributor` returns `HealthCheckResult`; aggregated by `IHealthService`.
 - Push announcements: `IHealthAnnouncer` + static `HealthReporter` (Degraded/Unhealthy/Healthy) with TTL and `lastNonHealthyAt`.
 - Enum: `HealthStatus { Healthy, Degraded, Unhealthy }`.
 - Criticality: modules can mark contributors as critical (future option surfacing; default: primary data, HTTP host).
 - Aggregation policy:
   - Any critical Unhealthy → overall Unhealthy (readiness fails).
   - Any non-critical Degraded/Unhealthy → overall Degraded (readiness degraded; liveness healthy).
