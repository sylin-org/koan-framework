---
type: GUIDE
domain: canon
title: "Canon Capabilities How-To"
audience: [developers, architects]
status: draft
last_updated: 2025-10-05
framework_version: v0.6.2
validation:
  status: not-yet-tested
  scope: docs/guides/canon-capabilities-howto.md
---

# Koan Canon Capabilities: End-to-End Guide

> **Contract**
> - **Inputs:** Koan application with `builder.Services.AddKoan()` and Koan data packages installed.
> - **Outputs:** Canon runtime configured with aggregation policies (including Source-of-Truth authorities), streaming identity graph resolution, audit, replay, and HTTP surfaces.
> - **Error Modes:** Missing aggregation keys, misconfigured Source-of-Truth declarations, lineage union conflicts, audit sink failures, replay capacity truncation.
> - **Success Criteria:** Canon entities persist with shared canonical IDs, aggregation indexes resolve duplicate identifiers, Source-of-Truth properties honor authoritative sources, policy footprints captured with authority evidence, replay returns ordered history.

This guide walks through Koan's Canon runtime, from a simple person profile merge to sophisticated multi-source identity graphs with audit trails. Imagine receiving employee data from HR, CRM, and badge systems—each using different identifiers (email, username, employee ID). Canon stitches these fragments into a single canonical record, resolves conflicting fields with explicit policies, and tracks every decision for compliance.

You'll start with basic aggregation keys and Latest-wins policies, then layer Source-of-Truth authorities for regulated fields, identity graph unions for flexible matching, and audit trails for explainability. Each section builds on the last, covering concepts, configuration, and real-world scenarios. Follow sequentially to build intuition, or jump ahead if you need a specific capability.

---

## Prerequisites

Add the canonical packages alongside the data core libraries:

```xml
<PackageReference Include="Koan.Canon.Domain" Version="0.6.2" />
<PackageReference Include="Koan.Data.Core" Version="0.6.2" />
<PackageReference Include="Koan.Data.Abstractions" Version="0.6.2" />
```

Reference at least one storage adapter (PostgreSQL shown):

```xml
<ProjectReference Include="../src/Koan.Data.Connector.Postgres/Koan.Data.Connector.Postgres.csproj" />
```

Optional configuration (`appsettings.json`) to seed defaults:

```json
{
  "Koan": {
    "Canon": {
      "Replay": {
        "Capacity": 200
      }
    }
  }
}
```

`Program.cs` stays minimal—Canon contributions load through `AddKoan()` and module discovery:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

var app = builder.Build();

app.Run();
```

---

## 1. Foundations: Defining Entities and Basic Aggregation

**Concepts**

`CanonEntity<T>` represents the canonical "golden record" stitched from multiple upstream sources. Think of it like a person profile that starts with an HR email, gets enriched with a CRM username, then adds a badge system employee ID—all pointing to the same individual. `[AggregationKey]` tells Canon which fields identify duplicates, while `[AggregationPolicy]` resolves conflicts when multiple sources disagree.

Start with `Latest` (last write wins) for fields like display names and phone numbers. Graduate to `SourceOfTruth` when compliance demands that HR always wins for legal names and titles—no matter what other systems send.

**Recipe**

- Inherit from `CanonEntity<T>` (auto GUID v7 IDs, just like `Entity<T>`).
- Mark one or more `[AggregationKey]` properties—think email, username, employee ID. Each key is optional per payload; Canon unions them into an identity graph.
- Choose policies: `Latest` (simplest), `Min`/`Max` (numeric boundaries), `First` (immutable), or `SourceOfTruth` (authority required).
- For Source-of-Truth, provide `Source = "workday"` (single authority) or `Sources = new[] { "workday", "sap" }` (multi-authority) and pick a `Fallback` policy for pre-authority data.
- Optionally add `[Canon(audit: true)]` to capture policy footprints and change evidence for compliance dashboards.

**Simple Example: Latest-Wins Profile**

```csharp
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Model;

public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? DisplayName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? PhoneNumber { get; set; }
}

// Usage: Three sources send Alice's profile
var hrPayload = new PersonCanon
{
    Email = "alice@example.com",
    DisplayName = "Alice Smith",
    PhoneNumber = "555-1001"
};
await runtime.Canonize(hrPayload, CanonizationOptions.Default.WithOrigin("hr"));

var crmPayload = new PersonCanon
{
    Email = "alice@example.com",  // Same key → merges
    DisplayName = "Alice S.",      // Latest wins
    PhoneNumber = null             // Nulls don't overwrite
};
await runtime.Canonize(crmPayload, CanonizationOptions.Default.WithOrigin("crm"));

// Result: DisplayName = "Alice S.", PhoneNumber = "555-1001" (preserved)
```

**Advanced: Multi-Authority with Audit**

```csharp
[Canon(audit: true)]  // Captures policy footprints for compliance
public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? Username { get; set; }

    [AggregationKey]
    public string? EmployeeId { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? PreferredName { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Sources = new[] { "workday", "sap" },  // Either HR system wins
        Fallback = AggregationPolicyKind.Latest)]  // Before HR integration
    public string? LegalName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Max)]
    public DateTimeOffset? LastReviewDate { get; set; }
}
```

**Usage Scenarios**

Applications receive employee fragments from HR (email only), badge systems (employee ID only), and CRM (username only). Canon unions them into a single profile without requiring upfront data coordination. Marketing teams display user-friendly preferred names while legal holds onto immutable legal names from Workday. Compliance dashboards query audit footprints to answer "Who changed this field and why?" during regulatory reviews.

### When to Use Each Policy

| Policy | Use When | Example |
|--------|----------|---------|
| **Latest** | Field changes frequently, last write wins | Display names, phone numbers, preferences |
| **First** | Field is immutable after initial set | Account creation date, original source system |
| **Min/Max** | Numeric boundaries matter | Earliest hire date, highest salary, max credit limit |
| **SourceOfTruth** | Compliance requires authoritative source | Legal names, titles, SSN, regulatory fields |

**Quick Decision Tree**

```
Does compliance/legal require a specific system to own this field?
├─ YES → SourceOfTruth (specify Source = "workday")
└─ NO → Is the field immutable?
    ├─ YES → First
    └─ NO → Is it numeric with boundaries?
        ├─ YES → Min or Max
        └─ NO → Latest (default)
```

**Edge Considerations**

⚠️ **Null handling**: Canon ignores null keys for a specific payload but preserves them when present, allowing the identity graph to grow over time. A payload with only `Email` contributes that edge; later, a payload with only `EmployeeId` adds another edge—both link to the same canonical ID.

⚠️ **Identity unions**: When multiple non-null keys resolve to different canon IDs (Alice's email points to ID `abc`, her employee ID points to ID `xyz`), the runtime unions them: the lowest ID wins (`abc`), `xyz` gets marked `Superseded`, and a `identity:merged-from` tag records the lineage. Use this to detect and reconcile split identities.

⚠️ **Authority evidence**: Policy footprints include `authority=incoming` (authoritative source sent data), `existing` (authoritative value already present), or `fallback` (pre-authority, fallback policy applied). Audit sinks inherit this evidence for compliance queries like "Show all fields where non-authority sources attempted overrides."

✅ **Correlation IDs**: Set `CanonizationOptions.CorrelationId` to your business event ID (batch job, API request). Audit trails and replay history reference this ID, enabling end-to-end traceability from ingestion to canonical decision.

### Deep Dive: Source-of-Truth Policies

**Why This Matters**

Imagine GDPR compliance: your legal name must match government records, and only HR systems have authority to update it. Marketing can't override it, even accidentally. Source-of-Truth policies enforce this at the data layer—attempts to override trigger audit events, and the authoritative value always wins.

**Progressive Disclosure: Start Simple**

```csharp
// Single authority: Only Workday can update legal names
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Source = "workday")]
public string? LegalName { get; set; }

// If CRM sends a legal name, Canon rejects it silently and logs the attempt
var crmPayload = new PersonCanon { Email = "bob@example.com", LegalName = "Bobby" };
await runtime.Canonize(crmPayload, CanonizationOptions.Default.WithOrigin("crm"));
// Result: LegalName remains null or previous Workday value, audit shows "crm attempted override"
```

**Multi-Authority for Migrations**

```csharp
// During SAP → Workday migration, both systems are authoritative
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Sources = new[] { "workday", "sap" },
    Fallback = AggregationPolicyKind.Latest)]
public string? LegalName { get; set; }

// Before migration: Latest wins (legacy feeds accepted)
// During migration: Either Workday or SAP wins (both trusted)
// After migration: Remove "sap" from Sources array
```

**Validation and Guardrails**

Canon fails fast during startup if you misconfigure Source-of-Truth:

```csharp
// ❌ Missing authority
[AggregationPolicy(AggregationPolicyKind.SourceOfTruth)]
public string? Field { get; set; }
// Throws: "SourceOfTruth policy requires Source or Sources"

// ❌ Circular fallback
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Source = "workday",
    Fallback = AggregationPolicyKind.SourceOfTruth)]
public string? Field { get; set; }
// Throws: "SourceOfTruth cannot be its own fallback"

// ✅ Valid configuration
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Source = "workday",
    Fallback = AggregationPolicyKind.Latest)]
public string? Field { get; set; }
```

**Authority Evidence in Action**

```csharp
var options = CanonizationOptions.Default.WithOrigin("workday");
var result = await runtime.Canonize(hrPayload, options);

var nameFootprint = result.Metadata.PropertyFootprints[nameof(PersonCanon.LegalName)];
Console.WriteLine($"Policy: {nameFootprint.Policy}");
Console.WriteLine($"Authority: {nameFootprint.Evidence["authority"]}");
// Output: Policy: SourceOfTruth, Authority: incoming

// Later, CRM tries to override
var crmOptions = CanonizationOptions.Default.WithOrigin("crm");
var crmResult = await runtime.Canonize(crmPayload, crmOptions);
var crmFootprint = crmResult.Metadata.PropertyFootprints[nameof(PersonCanon.LegalName)];
Console.WriteLine($"Authority: {crmFootprint.Evidence["authority"]}");
// Output: Authority: existing (Workday value preserved, CRM rejected)
```

**Best Practices**

✅ **Document your authorities**: Maintain a README section listing which systems own which fields. Example: "LegalName, Title, HireDate → Workday; Budget, Department → SAP."

✅ **Use configuration for authority lists**: Instead of hardcoding `Sources = new[] { "workday", "sap" }`, read from `appsettings.json`:

```json
{
  "Canon": {
    "Authorities": {
      "HR": ["workday", "sap"],
      "Finance": ["netsuite"]
    }
  }
}
```

✅ **Expose authority map via API**: Your `CanonModelsController` automatically surfaces `AggregationPolicyDetails`—use it to build admin UIs showing which fields are locked to which sources.

⚠️ **Audit sink failures halt canonization**: If your audit sink throws (database down, network timeout), canonization fails to prevent lost evidence. Wrap sinks with resilience policies (retry, circuit breaker) or use queued sinks for high-volume scenarios.

### Identity Graph Alignment: Optional Keys, Flexible Unions

**The Problem**

Traditional identity resolution assumes a primary key (SSN, employee ID) that's always present. Real-world data is messy: HR sends email and employee ID, badge systems send only employee ID, CRM sends only username. You need flexible matching where any identifier can appear independently, and Canon stitches them together over time.

**How Canon Handles It**

Every `[AggregationKey]` is a peer—no primary/secondary hierarchy. Each non-null key contributes an edge in the identity graph. When multiple keys point to different canonical IDs, Canon unions them:

```csharp
// Day 1: HR sends email + employee ID
var hrPayload = new PersonCanon
{
    Email = "alice@example.com",
    EmployeeId = "31991"
};
await runtime.Canonize(hrPayload, opts.WithOrigin("hr"));
// Creates canonical ID: abc, indexes: Email=alice@... → abc, EmployeeId=31991 → abc

// Day 2: Badge system sends only employee ID (matches existing)
var badgePayload = new PersonCanon { EmployeeId = "31991" };
await runtime.Canonize(badgePayload, opts.WithOrigin("badge"));
// Resolves to same canonical ID: abc (via EmployeeId index)

// Day 3: CRM sends username + email (email matches, username is new)
var crmPayload = new PersonCanon
{
    Email = "alice@example.com",
    Username = "asmith"
};
await runtime.Canonize(crmPayload, opts.WithOrigin("crm"));
// Adds new edge: Username=asmith → abc

// Day 4: Legacy system sends conflicting data (username points to different ID)
var legacyPayload = new PersonCanon
{
    Username = "asmith",  // Points to ID abc
    EmployeeId = "99999"  // Previously pointed to ID xyz
};
await runtime.Canonize(legacyPayload, opts.WithOrigin("legacy"));
// Canon detects split identity:
// - Lowest ID wins: abc survives
// - xyz marked Superseded
// - Lineage event: "Merged xyz → abc (keys: Username=asmith, EmployeeId=99999)"
// - Tag added: identity:merged-from=xyz
```

**Lineage and Audit Trail**

```csharp
// After union, query the canonical record
var canonical = await PersonCanon.Get("abc");
var mergedFrom = canonical.Tags["identity:merged-from"];
Console.WriteLine($"This identity absorbed: {mergedFrom}"); // Output: xyz

// Audit sink captures the merge
// AuditEntry {
//   CanonicalId = "abc",
//   Phase = "Aggregation",
//   Event = "IdentityMerge",
//   Evidence = {
//     "supersededId": "xyz",
//     "winningKeys": "Username=asmith|EmployeeId=99999",
//     "reason": "CanonicalIdUnion"
//   }
// }
```

**Merge Posture: Auto-Union vs. Manual Review**

```csharp
// Default: Auto-union (lowest ID wins)
var autoOptions = CanonizationOptions.Default; // Identity.MergePosture = AutoUnion

// Require manual review for high-stakes merges
var manualOptions = CanonizationOptions.Default with
{
    Identity = new IdentityOptions { MergePosture = MergePosture.RequireManualReview }
};

var result = await runtime.Canonize(conflictPayload, manualOptions);
if (result.Outcome == CanonizationOutcome.RequiresReview)
{
    // Park for analyst: "EmployeeId=31991 and Username=asmith point to different IDs"
    await ParkForReview(result);
}
```

**Performance Characteristics**

| Scenario | Lookups | Write Operations | Typical Duration |
|----------|---------|------------------|------------------|
| Single key match | 1 index read | 1 canonical write | ~5ms |
| Composite key (3 keys) | 3 index reads | 1 canonical + 3 index writes | ~15ms |
| Identity union (2 IDs) | 6 index reads | 2 canonical reads + 1 merge write + 6 index updates | ~50ms |
| Complex union (5 IDs) | 15 index reads | 5 canonical reads + 1 merge write + 15 index updates | ~120ms |

**Best Practices**

✅ **Start with required keys**: Even though all keys are optional per payload, define at least 2-3 aggregation keys to reduce false positives. Example: Email + Username + EmployeeId.

✅ **Monitor merge frequency**: High merge rates (>5% of canonizations) suggest upstream data quality issues. Add validation rules to reject payloads with suspicious key combinations.

✅ **Use correlation IDs for lineage**: Set `CanonizationOptions.CorrelationId` to batch job IDs or request IDs. When unions occur, audit trails reference the source batches that triggered the merge.

⚠️ **Null keys don't block canonization**: A payload with all null keys throws `InvalidOperationException`. At least one key must be non-null to participate in the identity graph.

⚠️ **Index normalization**: After union, Canon rewrites all indexes to point to the winning canonical ID. Queries for superseded IDs automatically redirect to the canonical survivor.

### Policy Discovery Helpers: Type-Safe Metadata Queries

**Why This Matters**

Pipeline contributors, admin UIs, and validation logic often need to query aggregation policies: "Is this field Source-of-Truth? Which sources are authoritative? What's the fallback?" Reflection-based lookups are brittle and verbose. Canon provides strongly-typed helpers with expression-based selectors.

**Progressive Disclosure: Start Simple**

```csharp
// Get metadata for a canon model
var metadata = CanonModelAggregationMetadata.For<PersonCanon>();

// Query by property name (string-based, simple)
if (metadata.TryGetPolicy("LegalName", out var policy))
{
    Console.WriteLine($"Policy: {policy.Kind}");
    Console.WriteLine($"Authoritative sources: {string.Join(", ", policy.AuthoritativeSources)}");
}

// Require policy (throws if missing)
var namePolicy = metadata.GetRequiredPolicy("LegalName");
if (namePolicy.HasAuthoritativeSources)
{
    foreach (var source in namePolicy.AuthoritativeSources)
    {
        Console.WriteLine($"Authority: {source}");
    }
}
```

**Advanced: Expression-Based Selectors**

```csharp
// Type-safe property selector (compile-time safety)
var legalNamePolicy = metadata.GetRequiredPolicy((PersonCanon p) => p.LegalName);
Console.WriteLine($"Kind: {legalNamePolicy.Kind}");

// Check if specific source is authoritative
if (legalNamePolicy.IsAuthoritativeSource("workday"))
{
    Console.WriteLine("Workday is an authority for LegalName");
}

// Fallback policy (null if no fallback configured)
if (legalNamePolicy.Fallback.HasValue)
{
    Console.WriteLine($"Fallback: {legalNamePolicy.Fallback.Value}");
}

// Optional lookup (returns null if no policy)
var displayNamePolicy = metadata.TryGetPolicy((PersonCanon p) => p.DisplayName);
if (displayNamePolicy != null)
{
    Console.WriteLine($"DisplayName policy: {displayNamePolicy.Kind}");
}
```

**Real-World Usage**

```csharp
// Validation contributor: Reject non-authority attempts for Source-of-Truth fields
public sealed class AuthorityValidator : ICanonPipelineContributor<PersonCanon>
{
    public ValueTask ExecuteAsync(CanonPipelineContext<PersonCanon> context, CancellationToken ct)
    {
        var metadata = CanonModelAggregationMetadata.For<PersonCanon>();
        var legalNamePolicy = metadata.GetRequiredPolicy((PersonCanon p) => p.LegalName);

        if (legalNamePolicy.Kind == AggregationPolicyKind.SourceOfTruth
            && !string.IsNullOrEmpty(context.Entity.LegalName)
            && !legalNamePolicy.IsAuthoritativeSource(context.Options.Origin ?? "unknown"))
        {
            throw new InvalidOperationException(
                $"Only {string.Join(", ", legalNamePolicy.AuthoritativeSources)} can update LegalName. " +
                $"Origin '{context.Options.Origin}' is not authoritative.");
        }

        return ValueTask.CompletedTask;
    }
}

// Admin UI: Highlight Source-of-Truth fields
public IActionResult ShowPolicyMatrix()
{
    var metadata = CanonModelAggregationMetadata.For<PersonCanon>();
    var fields = new List<FieldPolicy>();

    foreach (var (propertyName, policy) in metadata.AggregationPolicyDetails)
    {
        fields.Add(new FieldPolicy
        {
            Name = propertyName,
            Kind = policy.Kind.ToString(),
            IsLocked = policy.HasAuthoritativeSources,
            Authorities = policy.AuthoritativeSources?.ToList() ?? new List<string>(),
            Fallback = policy.Fallback?.ToString()
        });
    }

    return View(fields); // Render table with locked field icons
}
```

**Validation and Guardrails**

```csharp
// ❌ Invalid property selector
var metadata = CanonModelAggregationMetadata.For<PersonCanon>();
var badPolicy = metadata.GetRequiredPolicy((PersonCanon p) => p.Id);
// Throws: "Property 'Id' does not have an aggregation policy"

// ❌ Wrong model type
var deviceMetadata = CanonModelAggregationMetadata.For<DeviceCanon>();
var wrongPolicy = deviceMetadata.GetRequiredPolicy((PersonCanon p) => p.LegalName);
// Throws: "Expression references PersonCanon but metadata is for DeviceCanon"

// ✅ Graceful optional lookup
var optionalPolicy = metadata.TryGetPolicy((PersonCanon p) => p.PhoneNumber);
if (optionalPolicy == null)
{
    Console.WriteLine("PhoneNumber has no aggregation policy (defaults to Latest)");
}
```

**API Surface: Automatic Exposure**

Controllers automatically surface aggregation policy details:

```csharp
[Route("api/canon/persons")]
public sealed class PersonsController : CanonModelsController<PersonCanon>
{
    // GET /api/canon/persons/metadata returns:
    // {
    //   "aggregationPolicyDetails": {
    //     "LegalName": {
    //       "kind": "SourceOfTruth",
    //       "authoritativeSources": ["workday", "sap"],
    //       "fallback": "Latest"
    //     },
    //     "PreferredName": {
    //       "kind": "Latest",
    //       "authoritativeSources": null,
    //       "fallback": null
    //     }
    //   }
    // }
}
```

**Best Practices**

✅ **Use expressions in contributors**: Type-safe selectors catch property renames at compile time. String-based lookups break silently during refactors.

✅ **Cache metadata instances**: `CanonModelAggregationMetadata.For<T>()` is fast (dictionary lookup) but not free. Store the result in a field if you query it frequently.

✅ **Expose policies to UIs**: Admin panels can highlight locked fields, show authority lists, and warn users when they attempt non-authority updates.

⚠️ **Metadata is immutable**: Policy descriptors reflect attribute declarations. Runtime changes require redeployment—don't attempt dynamic policy updates.

---

## 2. Pipeline Orchestration: Validation, Enrichment, and Observers

**Concepts**

Pipelines flow through six phases: `Intake → Validation → Aggregation → Policy → Projection → Distribution`. Each phase runs contributors in order—think middleware for canonicalization. Use pipelines to reject invalid payloads early (Validation), enrich metadata (Intake), and tag projections for view rebuilds (Projection). Observers sit outside the flow, capturing diagnostics without blocking progress.

Think of pipelines like ASP.NET middleware: early phases guard the door (authentication, validation), middle phases do the work (aggregation, policy resolution), late phases distribute results (projections, events). Unlike middleware, Canon pipelines are strongly typed and phase-aware—you can't accidentally run validation after policy resolution.

**Recipe**

- Implement `ICanonRuntimeConfigurator` to register your pipeline steps.
- Call `pipeline.AddStep(phase, contributor, message)` for each step.
- Attach observers with `pipeline.AddObserver()` for diagnostics, logging, or telemetry.
- Register audit sinks with `builder.UseAuditSink()` when `[Canon(audit: true)]` is present.

**Sample**

```csharp
using System.Linq;
using Koan.Canon.Domain.Runtime;

public sealed class PersonCanonRegistrar : ICanonRuntimeConfigurator
{
    public void Configure(CanonRuntimeBuilder builder)
    {
        builder.ConfigurePipeline<PersonCanon>(pipeline =>
        {
            pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
            {
                context.Metadata.RecordSource(context.Options.Origin ?? "people-feed", source =>
                {
                    source.Channel = "ingest";
                    source.SetAttribute("payloadId", context.Entity.Id);
                });
                return ValueTask.CompletedTask;
            }, "Intake registered source context");

            pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
            {
                var identity = context.Entity;
                if (string.IsNullOrWhiteSpace(identity.Email)
                    && string.IsNullOrWhiteSpace(identity.Username)
                    && string.IsNullOrWhiteSpace(identity.EmployeeId))
                {
                    context.Metadata.SetTag("identity:status", "missing");
                    throw new InvalidOperationException("At least one identity token is required.");
                }
                return ValueTask.CompletedTask;
            }, "Validation ensured required keys");

            pipeline.AddStep(CanonPipelinePhase.Projection, (context, _) =>
            {
                context.Metadata.SetTag("projected", "true");
                context.Metadata.SetTag("identity:present", string.Join(",",
                    new[] { context.Entity.Email, context.Entity.Username, context.Entity.EmployeeId }
                        .Where(v => !string.IsNullOrWhiteSpace(v))));
                return ValueTask.CompletedTask;
            }, "Projection tagged canonical payload");
        });

        builder.SetRecordCapacity(300); // Replay buffer size
        builder.UseAuditSink(new DefaultCanonAuditSink());
    }
}
```

**Usage Scenarios**

Applications reject payloads missing required keys (Validation) before wasting cycles on aggregation. Intake contributors tag payloads with source channel (API, batch, migration) for lineage tracking. Projection contributors mark canonical records for materialized view rebuilds—only process records with `projected=true` tag. Observers stream pipeline events to APM tools (Datadog, Application Insights) without blocking canonization.

### Pipeline Execution Order and Timing

| Phase | Purpose | Typical Duration | Example Use |
|-------|---------|------------------|-------------|
| **Intake** | Record source, correlations | <1ms | Tag origin, capture batch ID |
| **Validation** | Reject invalid payloads | 1-2ms | Check required keys, format validation |
| **Aggregation** | Resolve canonical ID | 5-50ms | Load indexes, union identities |
| **Policy** | Resolve field conflicts | 2-10ms | Apply Latest, SourceOfTruth, Min/Max |
| **Projection** | Mark view updates | <1ms | Tag for search index rebuild |
| **Distribution** | Publish events | Variable | Send to message bus, webhooks |

**Edge Considerations**

⚠️ **SkipDistribution doesn't skip contributors**: Setting `CanonizationOptions.SkipDistribution = true` flags the result but still runs Distribution phase contributors. Add explicit guards in your contributors if you need conditional logic.

⚠️ **Observer errors bubble**: Unlike ASP.NET middleware, observer exceptions halt canonization. Use try-catch inside observers if you want best-effort diagnostics without blocking.

⚠️ **Contributors must be idempotent**: Canon may re-run phases during replay or manual rebuilds. Avoid side effects (sending emails, charging credit cards) inside contributors—use Distribution phase to publish events for downstream handlers.

✅ **Phase dependencies**: Aggregation and Policy phases are framework-managed—you can't remove or reorder them. Your contributors run before/after these core phases.

---

## 3. Metadata, Indexes, and Audit Trails: The Paper Trail

**Concepts**

Canon tracks rich metadata alongside canonical records: aggregation indexes (for duplicate detection), source attributions (who sent what), policy footprints (which policy won and why), and audit entries (change evidence for compliance). Think of indexes as a reverse lookup table—given `Email=alice@example.com`, which canonical ID owns it? Footprints answer "Why did Workday's legal name win over CRM's?" Audit trails satisfy regulators asking "Show me every field change for this person in Q3 2024."

Indexes are automatic (derived from `[AggregationKey]` properties), but metadata and audit require explicit configuration. Start with basic tags (source, correlation ID), graduate to policy footprints, then layer audit sinks for compliance-critical entities.

**Recipe**

- Use metadata helpers: `RecordSource()`, `SetTag()`, `RecordExternalId()` (all built-in).
- Inspect `CanonizationResult.Metadata.PropertyFootprints` to see which policies won.
- Configure `ICanonAuditSink` (e.g., `DefaultCanonAuditSink`) and add `[Canon(audit: true)]` to capture full change evidence.

**Sample**

```csharp
var options = CanonizationOptions.Default with { CorrelationId = "ingest-42" };
var person = new PersonCanon
{
    Email = "woody@example.com",
    Username = "woody",
    EmployeeId = "31991",
    FullName = "Sheriff Woody"
};

var result = await runtime.Canonize(person, options);

var indexKey = $"Email={person.Email}|Username={person.Username}|EmployeeId={person.EmployeeId}";
var index = persistence.FindIndex<PersonCanon>(indexKey);

if (index?.CanonicalId == result.Canonical.Id)
{
    Console.WriteLine("Composite aggregation succeeded.");
}

var statusFootprint = result.Metadata.PropertyFootprints[nameof(PersonCanon.FullName)];
Console.WriteLine($"Winner: {statusFootprint.Policy} via {statusFootprint.SourceKey}");
```

**Usage Scenarios**

Applications query indexes to route upstream data: "Does `Email=bob@example.com` already exist? Send update, not create." Compliance dashboards read audit sinks to answer "Show all times a non-authority source attempted to override LegalName." Reconciliation scripts compare policy footprints after identity unions: "After merge, did the correct values win?"

### Index Lookup Performance

| Operation | Typical Duration | Example |
|-----------|------------------|---------|
| Single key lookup | ~2ms | `FindIndex("Email=alice@example.com")` |
| Composite key lookup | ~5ms | `FindIndex("Email=...\|Username=...\|EmployeeId=...")` |
| Index write (single key) | ~3ms | Automatic during canonization |
| Index rewrite (union) | ~50ms per ID | After identity merge (6 indexes × 2 IDs) |

**Edge Considerations**

⚠️ **Index attributes include arrival tokens**: Each index stores `CreatedAt`, `UpdatedAt`, and `CorrelationId`. Use these for troubleshooting: "Which batch wrote this index? When?"

⚠️ **Audit sink failures halt canonization**: If your audit sink throws (database connection lost, disk full), canonization fails to prevent lost evidence. Wrap sinks with retry policies or circuit breakers. For high-volume systems, use asynchronous sinks (message queue) to decouple audit writes from canonization.

⚠️ **Metadata merge semantics**: When Canon merges metadata during identity unions, it uses `preferIncoming` by default (latest metadata wins). For historical replays, set `CanonizationOptions.Metadata.MergeStrategy = PreferExisting` to preserve original timestamps.

✅ **Property footprints capture decisions**: After canonization, inspect `result.Metadata.PropertyFootprints` to see which policy applied, which source won, and authority state. Use this in admin UIs to explain field values to end users.

---

## 4. Canonization Options, Staging, and Replay: Control and Observability

**Concepts**

`CanonizationOptions` controls each canonization run: origin (which source sent this?), tags (custom metadata), stage behavior (park for review?), requested views (trigger rebuilds?), and distribution flags (skip event publishing?). Think of it like HTTP request headers—every run carries context that influences behavior and downstream routing.

Staging mode defers canonicalization: payloads land in a staging table, awaiting manual review or batch processing. Useful for high-risk integrations (mergers, migrations) where you want human approval before committing to canonical state. Replay streams historical canonization events—use it for diagnostics, audits, or rebuilding materialized views after policy changes.

**Recipe**

- Start with `CanonizationOptions.Default` and layer customizations using `With*()` helpers or `with { }` syntax.
- Use `WithStageBehavior(CanonStageBehavior.StageOnly)` to park payloads without canonicalizing.
- Call `runtime.Replay(from, to, ct)` to stream canonization history (respects replay capacity configured in settings).

**Sample**

```csharp
var options = CanonizationOptions.Default
    .WithOrigin("crm-feed")
    .WithTag("priority", "high")
    .WithRequestedViews("summary", "exports")
    with
    {
        CorrelationId = Guid.NewGuid().ToString("n"),
        SkipDistribution = true
    };

var stageOptions = CanonizationOptions.Default
    .WithStageBehavior(CanonStageBehavior.StageOnly)
    with { CorrelationId = "batch-queue" };

var stageResult = await runtime.Canonize(device, stageOptions);
if (stageResult.Outcome == CanonizationOutcome.Parked)
{
    Console.WriteLine("Deferred canonicalization for manual review.");
}

await foreach (var record in runtime.Replay(from: DateTimeOffset.UtcNow.AddHours(-1)))
{
    Console.WriteLine($"[{record.OccurredAt:u}] {record.Phase} -> {record.Outcome}");
}
```

**Usage Scenarios**

Applications skip distribution during backfill operations—still canonize and audit, but don't publish events to downstream systems (`SkipDistribution = true`). High-risk integrations stage payloads for manual review: "Park all merger acquisition data, let analysts approve before merging into canonical state." Observability dashboards stream replay history to visualize ingestion latency: "Show canonization volume by hour for the last 7 days."

### Options Quick Reference

| Option | Purpose | Example |
|--------|---------|---------|
| **Origin** | Identify source system | `WithOrigin("workday")` |
| **CorrelationId** | Link to business event | `with { CorrelationId = batchJobId }` |
| **Tags** | Custom metadata | `WithTag("priority", "high")` |
| **StageBehavior** | Defer canonization | `WithStageBehavior(StageOnly)` |
| **RequestedViews** | Trigger view rebuilds | `WithRequestedViews("summary", "exports")` |
| **SkipDistribution** | Suppress event publishing | `with { SkipDistribution = true }` |
| **Identity.MergePosture** | Control union behavior | `AutoUnion` vs `RequireManualReview` |

**Edge Considerations**

⚠️ **Replay capacity truncates history**: Default capacity is 200 events per entity (configurable in `appsettings.json`). High-volume systems (millions of canonizations) should either raise capacity or implement external event streaming (write to message bus instead of Canon's in-memory buffer).

⚠️ **Stage metadata captures behavior**: When you use `StageOnly`, Canon adds `runtime:stage-behavior=StageOnly` tag to metadata. Use this to route staged payloads: "Process all staged records with priority=high tag."

⚠️ **Requested views trigger reprojection flags**: Setting `RequestedViews = ["summary", "exports"]` marks `CanonizationResult.ReprojectionTriggered = true`. View rebuild jobs should poll for these flags or subscribe to distribution events carrying requested view lists.

✅ **Options are immutable**: Clone defaults with `with { }` syntax. This ensures thread-safety and prevents accidental mutations across requests.

---

## 5. Web APIs: Exposing Canon Entities and Metadata

**Concepts**

`CanonModelsController<T>` extends `EntityController<T>` with canon-specific metadata endpoints: aggregation policy details, property footprints, and identity graph lineage. Use it to build admin consoles showing which fields are locked to which sources, or compliance dashboards tracking policy decisions. Controllers follow Koan's declarative routing pattern—inherit, configure, done. No inline `MapGet` endpoints, no manual CRUD plumbing.

Think of Canon controllers as Entity controllers with bonus metadata endpoints. Standard CRUD (GET, POST, PUT, DELETE) works the same, but you also get `/metadata` endpoints surfacing aggregation policies and `/history` endpoints streaming replay events.

**Recipe**

- Reference `Koan.Canon.Web` (includes controller base classes).
- Create controller inheriting `CanonModelsController<TCanon>`.
- Override `Configure()` only for custom response shaping or page sizes (follow WEB-0035 transformer patterns).

**Sample**

```csharp
using Koan.Canon.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

[Route("api/canon/persons")]
public sealed class PersonsController : CanonModelsController<PersonCanon>
{
    protected override void Configure(ControllerConfigurator<PersonCanon> configurator)
    {
        configurator.WithDefaultPageSize(50);
        configurator.TransformResult(result => new
        {
            result.Canonical.Id,
            result.Metadata.CanonicalId,
            result.Metadata.Tags
        });
    }
}
```

**Usage Scenarios**

Applications POST payloads to `/api/canon/persons` to trigger canonicalization (returns canonical ID, outcome, and metadata). Admin consoles GET `/api/canon/persons/{id}/metadata` to show policy footprints and authority evidence. Compliance dashboards query `/api/canon/persons/history` to stream recent canonization events for audit trails.

### Canon Controller Endpoints

| Endpoint | Method | Purpose | Response |
|----------|--------|---------|----------|
| `/api/canon/persons` | GET | List canonical records | Paged list with metadata |
| `/api/canon/persons/{id}` | GET | Get canonical record | Full entity + metadata |
| `/api/canon/persons` | POST | Canonize payload | CanonizationResult (ID, outcome, footprints) |
| `/api/canon/persons/{id}` | PUT | Update and re-canonize | Updated canonical record |
| `/api/canon/persons/{id}` | DELETE | Remove canonical record | Success status |
| `/api/canon/persons/metadata` | GET | Aggregation policy details | Policy descriptors (authorities, fallbacks) |
| `/api/canon/persons/history` | GET | Replay canonization events | Streamed event log |

**Real-World Example**

```csharp
// Admin UI: Show which fields are locked to authorities
public async Task<IActionResult> ShowLockedFields()
{
    var response = await _httpClient.GetFromJsonAsync<MetadataResponse>(
        "/api/canon/persons/metadata");

    var lockedFields = response.AggregationPolicyDetails
        .Where(p => p.Value.HasAuthoritativeSources)
        .Select(p => new {
            Field = p.Key,
            Authorities = string.Join(", ", p.Value.AuthoritativeSources),
            Fallback = p.Value.Fallback?.ToString() ?? "None"
        });

    return View(lockedFields);
}

// Compliance dashboard: Show recent overrides
public async Task<IActionResult> ShowRecentOverrides()
{
    var history = await _httpClient.GetFromJsonAsync<List<CanonHistoryEvent>>(
        "/api/canon/persons/history?from=2024-10-01&to=2024-10-06");

    var overrides = history
        .Where(e => e.Evidence.ContainsKey("authority") && e.Evidence["authority"] == "existing")
        .Select(e => new {
            e.CanonicalId,
            e.OccurredAt,
            Field = e.Evidence["field"],
            AttemptedBy = e.Evidence["source"]
        });

    return View(overrides);
}
```

**Edge Considerations**

⚠️ **Security**: Canon controllers inherit Entity controller authorization. Apply `[Authorize]` attributes, tenant filters, or custom policies to restrict access. Remember: canonical records aggregate data from multiple sources—ensure end users have permission to see all contributing systems.

⚠️ **Response shaping**: Override `Configure()` to customize responses. Example: Hide internal metadata fields, transform IDs for external APIs, or redact sensitive properties.

```csharp
protected override void Configure(ControllerConfigurator<PersonCanon> configurator)
{
    configurator.TransformResult(result => new
    {
        result.Canonical.Id,
        result.Canonical.Email,
        result.Canonical.DisplayName,
        Sources = result.Metadata.Tags
            .Where(t => t.Key.StartsWith("source:"))
            .Select(t => t.Value)
    });
}
```

✅ **Auto-discovery**: Canon controllers register automatically via `AddKoan()`. No need for explicit `services.AddControllers()` or assembly scanning—Koan handles it.

✅ **Replay pagination**: `/history` endpoints default to last 200 events (configurable). For large historical queries, implement external event streaming (write to message bus, query from data warehouse).

---

---

## Next Steps

You've built a foundation: canonical entities with flexible aggregation keys, Source-of-Truth policies pinning regulated fields to authorities, identity graph unions stitching fragments into golden records, and audit trails satisfying compliance. Here's how to extend it:

**Enrich with Vectors**: Once canonical profiles stabilize, add vector embeddings for semantic search. Reference `Koan.Data.Vector.Abstractions` and embed canonical fields (descriptions, profiles) for similarity queries.

**Correlate with Telemetry**: Combine Canon replay with APM tools (Datadog, Application Insights). Stream canonization events to correlate ingestion latency with upstream SLA violations.

**Custom Contributors**: Extend pipelines with domain-specific logic—validate business rules, enrich with external APIs, or route high-risk payloads to manual review queues. Keep contributors deterministic and side-effect free (publish events in Distribution phase).

**Multi-Tenant Isolation**: Apply Entity context partitioning to Canon entities. Use `EntityContext.Partition("tenant-abc")` to scope canonization runs, indexes, and audit trails per tenant.

**Event-Driven Downstream**: Subscribe to canon distribution events (change notifications) to rebuild materialized views, trigger workflows, or sync to external systems. Canon publishes events carrying canonical ID, outcome, and requested views—use these to drive downstream automation.

---

## Key Takeaways

✅ **Start simple**: Begin with `Latest` policies and single aggregation keys. Graduate to Source-of-Truth and composite keys as complexity demands.

✅ **Identity graphs are flexible**: Every `[AggregationKey]` is optional per payload. Canon unions fragments over time—embrace eventual consistency.

✅ **Policies explain decisions**: Property footprints answer "Why did this value win?" Authority evidence proves "Was this from an authoritative source?"

✅ **Audit trails satisfy compliance**: `[Canon(audit: true)]` captures full change evidence. Query audit sinks to reconstruct decision history for regulators.

✅ **Metadata drives UIs**: Expose aggregation policy details via `/metadata` endpoints. Build admin consoles highlighting locked fields and authority mappings.

When in doubt, stick to the patterns above: explicit aggregation keys, well-defined policies, and concise pipelines. They ensure predictable merges, trustworthy audit evidence, and maintainable web surfaces—just like Entity patterns ensure provider-agnostic data access and Flow patterns ensure deterministic orchestration. Canon is the missing piece for multi-source aggregation with compliance built in.
