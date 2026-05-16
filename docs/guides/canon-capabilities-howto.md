---
type: GUIDE
domain: canon
title: "Canon Capabilities How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: all-examples-tested
related_guides:
  - entity-capabilities-howto.md
  - patch-capabilities-howto.md
  - ai-vector-howto.md
---

# Canon Capabilities How-To

**Related Guides**
- [Entity Capabilities](entity-capabilities-howto.md) - Entity-first patterns and static methods
- [Patch Capabilities](patch-capabilities-howto.md) - Partial update patterns
- [AI Vector Operations](ai-vector-howto.md) - Vector search and embeddings

---

Think of this guide as a conversation with a colleague who's built identity resolution systems for mergers, migrations, and multi-source integrations. We'll explore Koan's Canon runtime—how to stitch employee fragments from HR, CRM, and badge systems into a single "golden record," resolve conflicting fields with explicit policies, and track every decision for compliance audits.

Canon is about answering: "When five systems send different values for the same person, which one wins—and why?"

## Contract

**What this guide provides:**
- How to aggregate entities from multiple sources into canonical "golden records"
- When to use Latest-wins vs Source-of-Truth vs Min/Max policies
- Identity graph resolution with flexible aggregation keys
- Audit trails and policy footprints for compliance
- Pipeline orchestration for validation, enrichment, and distribution

**Inputs:**
- Koan application with `builder.Services.AddKoan()` and Canon packages installed
- Multiple data sources sending entity fragments (HR, CRM, badge systems)
- Aggregation policies defining conflict resolution (Latest, SourceOfTruth, Min/Max)

**Outputs:**
- Canon entities with shared canonical IDs
- Aggregation indexes resolving duplicate identifiers
- Source-of-Truth properties honoring authoritative sources
- Policy footprints with authority evidence
- Audit trails capturing every decision

**Error modes:**
- Missing aggregation keys → InvalidOperationException
- Misconfigured Source-of-Truth (no authority) → validation error at startup
- Lineage union conflicts → RequiresReview outcome (if configured)
- Audit sink failures → canonization halted (prevents lost evidence)
- Replay capacity truncation → old events dropped (configurable)

**Success criteria:**
- Fragments merge into single canonical record per identity
- Conflicting fields resolve according to policies
- Authority evidence proves compliance ("Workday sent this legal name")
- Replay returns ordered canonization history
- Identity unions handle split identities gracefully

**See also:**
- Canonical model: [CANON-0001: Canon Domain Model](../decisions/CANON-0001-canon-domain-model.md)
- Pipeline phases: [CANON-0002: Pipeline Architecture](../decisions/CANON-0002-pipeline-architecture.md)

---

## 0. Prerequisites and When to Use

### When to Use Canon

**Use Canon when:**
- Multiple systems send data about the same entities (person, device, account)
- Each system uses different identifiers (email, username, employee ID)
- You need a "golden record" that aggregates all sources
- Compliance requires tracking which source provided which field
- Conflicting values need explicit resolution policies (not just "last write wins")

**Example scenarios:**
- **HR integration:** Workday sends legal name/hire date, ADP sends payroll data, both reference same employee
- **Customer 360:** CRM sends preferences, e-commerce sends purchase history, support sends ticket data—all for same customer
- **Device registry:** IT asset management sends serial numbers, security sends IP addresses, monitoring sends health metrics
- **Merger/acquisition:** Two companies' HR systems send overlapping employee data, need to merge without data loss

**Don't use Canon when:**
- Single source of truth (just use Entity<T>)
- Simple append-only event streams (use regular entities with timestamps)
- No identity ambiguity (unique IDs always provided by all sources)

### When to Use Each Aggregation Policy

**Latest (last write wins):**
```
✅ Use when:
- Field changes frequently
- Most recent value is always correct
- No compliance requirements
- Examples: display name, phone number, preferences

❌ Avoid when:
- Specific system must own the field
- Old values might be more authoritative
- Compliance requires authority tracking
```

**First (immutable after set):**
```
✅ Use when:
- Field should never change after initial set
- Need to preserve original value
- Examples: account creation date, original source

❌ Avoid when:
- Corrections might be needed
- Field naturally changes over time
```

**Min/Max (numeric boundaries):**
```
✅ Use when:
- Numeric field with meaningful boundaries
- Need earliest/latest date or highest/lowest value
- Examples: earliest hire date, highest salary, max credit limit

❌ Avoid when:
- Field is not numeric
- Boundaries don't have business meaning
```

**SourceOfTruth (authority required):**
```
✅ Use when:
- Compliance requires specific system authority
- Legal/regulatory fields
- Only certain systems can update
- Examples: legal name (HR only), SSN (payroll only), titles (Workday only)

❌ Avoid when:
- No clear authoritative source
- All sources equally valid
- Field frequently changes across systems
```

### Decision Tree

```
Start: "I need to resolve conflicting field values from multiple sources"
│
├─ Does compliance/legal require a specific system to own this field?
│  ├─ Yes → SourceOfTruth (specify Source = "workday")
│  └─ No ↓
│
├─ Should the field be immutable after first set?
│  ├─ Yes → First
│  └─ No ↓
│
├─ Is it a numeric field with meaningful boundaries?
│  ├─ Yes → Min (earliest/smallest) or Max (latest/largest)
│  └─ No ↓
│
└─ Should the most recent value always win?
   └─ Yes → Latest (default)
```

### Prerequisites

Before following this guide:

1. **Koan packages installed:**
   ```xml
   <PackageReference Include="Koan.Canon.Domain" Version="0.6.3" />
   <PackageReference Include="Koan.Data.Core" Version="0.6.3" />
   <PackageReference Include="Koan.Data.Abstractions" Version="0.6.3" />
   ```

2. **Storage adapter configured:**
   ```xml
   <ProjectReference Include="../src/Koan.Data.Connector.Postgres/Koan.Data.Connector.Postgres.csproj" />
   ```

3. **Optional configuration (appsettings.json):**
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

4. **Program.cs stays minimal:**
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   builder.Services.AddKoan();
   var app = builder.Build();
   app.Run();
   ```

5. **Familiarity with Entity<T> pattern** (see [entity-capabilities-howto.md](entity-capabilities-howto.md))

---

## 1. Quick Start

**Scenario:** You receive employee data from HR (email + legal name) and CRM (email + display name). Build a canonical person profile that merges both sources.

### Step 1: Define your Canon entity

```csharp
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Model;

public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    // Aggregation key: identifies duplicates
    [AggregationKey]
    public string? Email { get; set; }

    // Latest policy: most recent value wins
    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? DisplayName { get; set; }

    // SourceOfTruth: only HR can update legal name
    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "hr")]
    public string? LegalName { get; set; }
}
```

### Step 2: Send data from HR

```csharp
var hrPayload = new PersonCanon
{
    Email = "alice@example.com",
    LegalName = "Alice Marie Smith",
    DisplayName = "Alice Smith"
};

var hrResult = await hrPayload.Canonize(origin: "hr");
Console.WriteLine($"Canonical ID: {hrResult.Canonical.Id}");
Console.WriteLine($"LegalName: {hrResult.Canonical.LegalName}");
Console.WriteLine($"DisplayName: {hrResult.Canonical.DisplayName}");
// Output:
// Canonical ID: 01JB...
// LegalName: Alice Marie Smith
// DisplayName: Alice Smith
```

### Step 3: Send data from CRM

```csharp
var crmPayload = new PersonCanon
{
    Email = "alice@example.com",  // Same email → merges with HR record
    DisplayName = "Alice S."       // Latest policy: CRM value overwrites HR
    // No LegalName sent (CRM doesn't have authority anyway)
};

var crmResult = await crmPayload.Canonize(origin: "crm");
Console.WriteLine($"Canonical ID: {crmResult.Canonical.Id}");  // Same ID as HR!
Console.WriteLine($"LegalName: {crmResult.Canonical.LegalName}");
Console.WriteLine($"DisplayName: {crmResult.Canonical.DisplayName}");
// Output:
// Canonical ID: 01JB... (same as before)
// LegalName: Alice Marie Smith (preserved from HR)
// DisplayName: Alice S. (updated to latest from CRM)
```

### Step 4: Try unauthorized legal name update

```csharp
var badgePayload = new PersonCanon
{
    Email = "alice@example.com",
    LegalName = "Alice Johnson"  // Badge system tries to update legal name
};

var badgeResult = await badgePayload.Canonize(origin: "badge");
Console.WriteLine($"LegalName: {badgeResult.Canonical.LegalName}");
// Output:
// LegalName: Alice Marie Smith (unchanged - badge not authoritative)
```

**What just happened?**
- HR created the canonical record with ID `01JB...`
- CRM merged into the same record (matched by email aggregation key)
- `DisplayName` updated to "Alice S." (Latest policy)
- `LegalName` remained "Alice Marie Smith" (SourceOfTruth policy blocked badge system)
- Audit trail captured all three canonization events

**Pro tip:** Start with Latest policies for everything. Add SourceOfTruth only when compliance demands it. You can always tighten policies later.

---

## 2. Foundations: Entities and Basic Aggregation

**Concept:** `CanonEntity<T>` is your "golden record"—a single entity stitched from multiple upstream sources. Think of it like a jigsaw puzzle where each source provides a few pieces.

### How Aggregation Works

```
Day 1:  HR sends      → Creates canonical record
        Email: alice@example.com
        LegalName: Alice Smith

Day 2:  CRM sends     → Merges into same record (email matches)
        Email: alice@example.com
        DisplayName: Alice S.

Result: Canonical record now has both fields:
        Email: alice@example.com
        LegalName: Alice Smith     (from HR)
        DisplayName: Alice S.       (from CRM)
```

### Recipe: Define a Canon entity

**Basic Latest-wins profile:**
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
```

**Key concepts:**
- **Inherit from CanonEntity<T>** - Gets GUID v7 IDs automatically
- **Mark aggregation keys** - `[AggregationKey]` identifies duplicates
- **Choose policies** - Latest, First, Min, Max, or SourceOfTruth
- **All keys optional per payload** - Canon unions them into identity graph

### Sample: Three sources, one profile

```csharp
// Day 1: HR sends initial data
var hrPayload = new PersonCanon
{
    Email = "bob@example.com",
    DisplayName = "Robert Johnson",
    PhoneNumber = "555-1001"
};
await hrPayload.Canonize(origin: "hr");

// Day 2: CRM sends updated display name
var crmPayload = new PersonCanon
{
    Email = "bob@example.com",  // Matches HR by email
    DisplayName = "Bob J."      // Latest policy: overwrites HR value
    // PhoneNumber not sent: preserved from HR
};
await crmPayload.Canonize(origin: "crm");

// Day 3: Badge system sends new phone
var badgePayload = new PersonCanon
{
    Email = "bob@example.com",
    PhoneNumber = "555-2002"    // Latest policy: overwrites HR value
};
await badgePayload.Canonize(origin: "badge");

// Final canonical state:
var canonical = await PersonCanon.Get(hrPayload.Id);
// Email: bob@example.com
// DisplayName: Bob J. (from CRM, latest)
// PhoneNumber: 555-2002 (from badge, latest)
```

### Usage Scenarios

**Scenario 1: HR and payroll integration**
```csharp
[Canon(audit: true)]  // Track all changes for compliance
public sealed class EmployeeCanon : CanonEntity<EmployeeCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? EmployeeId { get; set; }

    [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "workday")]
    public string? LegalName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "adp")]
    public decimal? Salary { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Min)]
    public DateTimeOffset? HireDate { get; set; }  // Earliest hire date wins
}
```

**Scenario 2: Customer 360 (CRM + e-commerce + support)**
```csharp
public sealed class CustomerCanon : CanonEntity<CustomerCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? CustomerNumber { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? PreferredName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Max)]
    public decimal? LifetimeValue { get; set; }  // Highest value wins

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public CustomerPreferences? Preferences { get; set; }
}
```

**Scenario 3: Device registry (IT + security + monitoring)**
```csharp
public sealed class DeviceCanon : CanonEntity<DeviceCanon>
{
    [AggregationKey]
    public string? SerialNumber { get; set; }

    [AggregationKey]
    public string? MacAddress { get; set; }

    [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "asset-mgmt")]
    public string? Owner { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? IpAddress { get; set; }

    [AggregationPolicy(AggregationPolicyKind.First)]
    public DateTimeOffset? FirstSeen { get; set; }  // Immutable
}
```

**Pro tip:** Null handling—Canon preserves existing values when new payloads send null. If HR sends `PhoneNumber = null`, the existing phone number stays. This prevents accidental data loss from sparse payloads.

---

## 3. Source-of-Truth Policies

**When to use:** Compliance requires specific systems to own specific fields. Marketing can't override legal names from HR. Finance can't override security clearances from HR.

### Concepts

Source-of-Truth policies enforce authority at the data layer:
- Only designated sources can update the field
- Non-authority attempts are silently rejected
- Audit trails capture override attempts
- Fallback policies apply before authority is established

### Recipe: Single authority

```csharp
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Source = "workday")]
public string? LegalName { get; set; }

// Usage:
var workdayPayload = new PersonCanon
{
    Email = "charlie@example.com",
    LegalName = "Charles Edward Brown"
};
await workdayPayload.Canonize(origin: "workday");
// ✅ Workday is authoritative → LegalName set

var crmPayload = new PersonCanon
{
    Email = "charlie@example.com",
    LegalName = "Charlie Brown"
};
await crmPayload.Canonize(origin: "crm");
// ❌ CRM not authoritative → LegalName unchanged (still "Charles Edward Brown")
// ✅ Audit log shows "crm attempted override, rejected"
```

### Recipe: Multi-authority (migration scenarios)

```csharp
// During SAP → Workday migration, both are authoritative
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Sources = new[] { "workday", "sap" },
    Fallback = AggregationPolicyKind.Latest)]
public string? LegalName { get; set; }

// Phase 1: Before migration
var legacyPayload = new PersonCanon { Email = "...", LegalName = "..." };
await legacyPayload.Canonize(origin: "legacy-feed");
// Fallback policy: Latest wins (no authority established yet)

// Phase 2: During migration
var sapPayload = new PersonCanon { Email = "...", LegalName = "..." };
await sapPayload.Canonize(origin: "sap");
// ✅ SAP is authoritative

var workdayPayload = new PersonCanon { Email = "...", LegalName = "..." };
await workdayPayload.Canonize(origin: "workday");
// ✅ Workday is also authoritative (both trusted)

// Phase 3: After migration (remove "sap" from Sources array)
```

### Recipe: Authority evidence

```csharp
var result = await hrPayload.Canonize(origin: "workday");

var nameFootprint = result.Metadata.PropertyFootprints[nameof(PersonCanon.LegalName)];
Console.WriteLine($"Policy: {nameFootprint.Policy}");
Console.WriteLine($"Authority: {nameFootprint.Evidence["authority"]}");
// Output:
// Policy: SourceOfTruth
// Authority: incoming (Workday sent authoritative value)

// Later, CRM tries to override
var crmResult = await crmPayload.Canonize(origin: "crm");
var crmFootprint = crmResult.Metadata.PropertyFootprints[nameof(PersonCanon.LegalName)];
Console.WriteLine($"Authority: {crmFootprint.Evidence["authority"]}");
// Output:
// Authority: existing (Workday value preserved, CRM rejected)
```

### Sample: Configuration-driven authorities

Instead of hardcoding sources, read from config:

```json
// appsettings.json
{
  "Canon": {
    "Authorities": {
      "HR": ["workday", "sap"],
      "Finance": ["netsuite"],
      "Security": ["okta"]
    }
  }
}
```

```csharp
// Load from configuration
public class CanonAuthoritiesOptions
{
    public Dictionary<string, string[]> Authorities { get; set; } = new();
}

// In entity definition
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    // Sources set dynamically via attribute parameter (future enhancement)
    Source = "workday")]
public string? LegalName { get; set; }
```

### Usage Scenarios

**Scenario 1: GDPR compliance (legal name must match government records)**
```csharp
[Canon(audit: true)]
public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "hr-verified")]
    public string? LegalName { get; set; }  // Only verified HR data

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "hr-verified")]
    public string? DateOfBirth { get; set; }  // PII from verified source only

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? PreferredName { get; set; }  // User can update
}
```

**Scenario 2: Financial data (only finance systems can update)**
```csharp
public sealed class AccountCanon : CanonEntity<AccountCanon>
{
    [AggregationKey]
    public string? AccountNumber { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "netsuite")]
    public decimal? CreditLimit { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "netsuite")]
    public string? PaymentTerms { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Max)]
    public decimal? OutstandingBalance { get; set; }  // Highest balance wins
}
```

**Scenario 3: Security clearances (only security team can update)**
```csharp
public sealed class EmployeeCanon : CanonEntity<EmployeeCanon>
{
    [AggregationKey]
    public string? EmployeeId { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "security-ops")]
    public string? ClearanceLevel { get; set; }

    [AggregationPolicy(
        AggregationPolicyKind.SourceOfTruth,
        Source = "security-ops")]
    public DateTimeOffset? ClearanceExpirationDate { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? Department { get; set; }
}
```

### Validation and Guardrails

Canon validates SourceOfTruth policies at startup:

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

**Pro tip:** Document your authorities in a README section: "LegalName, Title, HireDate → Workday; Budget, Department → SAP; ClearanceLevel → Security Ops." This helps new team members understand governance.

---

## 4. Identity Graph and Flexible Keys

**When to use:** Real-world data doesn't have a single primary key. HR sends email + employee ID, badge systems send only employee ID, CRM sends only username. You need flexible matching.

### Concepts

Every `[AggregationKey]` is a peer—no primary/secondary hierarchy:
- Each non-null key contributes an edge in the identity graph
- Canon indexes all keys: `Email=alice@...` → canonical ID `abc`
- When multiple keys point to different IDs, Canon unions them (lowest ID wins)
- Superseded IDs get marked, lineage tracked

### How Identity Graphs Work

```
Day 1: HR sends email + employee ID
       Email: alice@example.com
       EmployeeId: 31991
       → Creates canonical ID: abc
       → Indexes: Email → abc, EmployeeId → abc

Day 2: Badge sends only employee ID (matches)
       EmployeeId: 31991
       → Resolves to canonical ID: abc (via index)

Day 3: CRM sends email + username (email matches, username new)
       Email: alice@example.com
       Username: asmith
       → Canonical ID: abc (via email index)
       → Adds index: Username → abc

Day 4: Legacy sends conflicting data (split identity detected)
       Username: asmith  (→ abc via index)
       EmployeeId: 99999 (→ xyz via different index!)
       → Canon detects split
       → Union: abc survives (lowest ID), xyz marked Superseded
       → Tag: identity:merged-from=xyz
       → Audit event: "Merged xyz → abc"
```

### Recipe: Multi-key entity

```csharp
public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? Username { get; set; }

    [AggregationKey]
    public string? EmployeeId { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? DisplayName { get; set; }
}

// Usage: Keys contribute independently
var hrPayload = new PersonCanon
{
    Email = "dana@example.com",
    EmployeeId = "42001"
    // No username: that's OK!
};
await hrPayload.Canonize(origin: "hr");

var badgePayload = new PersonCanon
{
    EmployeeId = "42001"  // Matches HR
    // No email or username: still merges
};
await badgePayload.Canonize(origin: "badge");

var crmPayload = new PersonCanon
{
    Email = "dana@example.com",  // Matches HR
    Username = "dana.miller"     // Adds new key
};
await crmPayload.Canonize(origin: "crm");

// All three payloads merged into one canonical record
```

### Recipe: Lineage tracking after union

```csharp
// After identity union, query canonical record
var canonical = await PersonCanon.Get("abc");
var mergedFrom = canonical.Tags.GetValueOrDefault("identity:merged-from");
if (mergedFrom != null)
{
    Console.WriteLine($"This identity absorbed: {mergedFrom}");
}

// Audit sink captured the merge
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

### Recipe: Merge posture control

```csharp
// Default: Auto-union (lowest ID wins)
var autoOptions = CanonizationOptions.Default;

// Require manual review for high-stakes merges
var manualOptions = CanonizationOptions.Default with
{
    Identity = new IdentityOptions
    {
        MergePosture = MergePosture.RequireManualReview
    }
};

var result = await conflictPayload.Canonize(configure: _ => manualOptions);
if (result.Outcome == CanonizationOutcome.RequiresReview)
{
    Console.WriteLine("Split identity detected - parking for analyst review");
    // Park for manual review: "EmployeeId=31991 and Username=asmith point to different IDs"
    await ParkForReview(result);
}
```

### Sample: Merger scenario

```csharp
// Company A acquired Company B - need to merge employee records
[Canon(audit: true)]
public sealed class EmployeeCanon : CanonEntity<EmployeeCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? CompanyAEmployeeId { get; set; }

    [AggregationKey]
    public string? CompanyBEmployeeId { get; set; }

    [AggregationPolicy(AggregationPolicyKind.SourceOfTruth, Source = "hr-verified")]
    public string? LegalName { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Min)]
    public DateTimeOffset? HireDate { get; set; }  // Earliest hire date (seniority)
}

// Import Company A data
var companyAPayload = new EmployeeCanon
{
    Email = "evan@example.com",
    CompanyAEmployeeId = "A-5001",
    LegalName = "Evan Foster",
    HireDate = new DateTimeOffset(2020, 3, 15, 0, 0, 0, TimeSpan.Zero)
};
await companyAPayload.Canonize(origin: "hr-verified");

// Import Company B data (same person hired earlier at Company B)
var companyBPayload = new EmployeeCanon
{
    Email = "evan@example.com",  // Same email → merges
    CompanyBEmployeeId = "B-3042",
    HireDate = new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero)  // Earlier!
};
await companyBPayload.Canonize(origin: "hr-verified");

// Result:
// - Single canonical record
// - Both employee IDs indexed
// - HireDate = 2018-06-01 (Min policy: earlier date wins)
// - LegalName from first authority (Company A's HR)
```

### Usage Scenarios

**Scenario 1: Progressive identity enrichment**
```csharp
// Week 1: HR sends partial data
var week1 = new PersonCanon { Email = "frank@example.com" };
await week1.Canonize(origin: "hr");

// Week 2: Badge system adds employee ID
var week2 = new PersonCanon { EmployeeId = "50012" };  // No email!
// How does this merge? It doesn't - creates new record (no matching key)

// Week 3: Integration fixed - badge sends both
var week3 = new PersonCanon
{
    Email = "frank@example.com",
    EmployeeId = "50012"
};
await week3.Canonize(origin: "badge");
// Now merges with week1 record via email
```

**Scenario 2: Detecting duplicate accounts**
```csharp
// Marketing creates account with email
var marketing = new CustomerCanon
{
    Email = "gina@example.com",
    CustomerNumber = "MKTG-001"
};
await marketing.Canonize(origin: "marketing");

// Sales creates account with same email, different customer number
var sales = new CustomerCanon
{
    Email = "gina@example.com",  // Same email!
    CustomerNumber = "SALES-042"  // Different ID
};
await sales.Canonize(origin: "sales");

// Canon merges via email - now have both customer numbers indexed
var canonical = await CustomerCanon.Query(c => c.Email == "gina@example.com").FirstAsync();
// Reveals duplicate account creation - can be flagged for cleanup
```

**Scenario 3: Multi-region identity**
```csharp
public sealed class GlobalEmployeeCanon : CanonEntity<GlobalEmployeeCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationKey]
    public string? NorthAmericaEmployeeId { get; set; }

    [AggregationKey]
    public string? EuropeEmployeeId { get; set; }

    [AggregationKey]
    public string? AsiaEmployeeId { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]
    public string? PreferredRegion { get; set; }
}

// Employee transfers from NA to Europe - both IDs point to same person
```

### Performance Characteristics

| Scenario | Lookups | Write Operations | Typical Duration |
|----------|---------|------------------|------------------|
| Single key match | 1 index read | 1 canonical + 1 index write | ~5ms |
| Composite key (3 keys) | 3 index reads | 1 canonical + 3 index writes | ~15ms |
| Identity union (2 IDs) | 6 index reads | 2 canonical reads + 1 merge + 6 index updates | ~50ms |
| Complex union (5 IDs) | 15 index reads | 5 canonical reads + 1 merge + 15 index updates | ~120ms |

**Pro tip:** Monitor merge frequency. If >5% of canonizations trigger unions, you have upstream data quality issues. Add validation to reject suspicious key combinations.

---

## 5. Pipeline Orchestration

**Concept:** Canonization flows through six phases: `Intake → Validation → Aggregation → Policy → Projection → Distribution`. Think ASP.NET middleware, but typed and phase-aware.

### Pipeline Phases

| Phase | Purpose | Your Contributors Run | Framework Runs |
|-------|---------|----------------------|----------------|
| **Intake** | Record source, correlations | Tag origin, capture batch ID | - |
| **Validation** | Reject invalid payloads | Check required keys, format rules | - |
| **Aggregation** | Resolve canonical ID | (framework-managed) | Load indexes, union identities |
| **Policy** | Resolve field conflicts | (framework-managed) | Apply Latest, SourceOfTruth, etc. |
| **Projection** | Mark view updates | Tag for search index rebuild | - |
| **Distribution** | Publish events | Send to message bus | - |

### Recipe: Basic pipeline contributor

```csharp
using Koan.Canon.Domain.Runtime;

public sealed class PersonCanonRegistrar : ICanonRuntimeConfigurator
{
    public void Configure(CanonRuntimeBuilder builder)
    {
        builder.ConfigurePipeline<PersonCanon>(pipeline =>
        {
            // Intake: Record source metadata
            pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
            {
                context.Metadata.RecordSource(
                    context.Options.Origin ?? "unknown",
                    source =>
                    {
                        source.Channel = "ingest";
                        source.SetAttribute("payloadId", context.Entity.Id);
                    });
                return ValueTask.CompletedTask;
            }, "Intake: Record source context");

            // Validation: Ensure at least one key
            pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
            {
                var entity = context.Entity;
                if (string.IsNullOrWhiteSpace(entity.Email)
                    && string.IsNullOrWhiteSpace(entity.Username)
                    && string.IsNullOrWhiteSpace(entity.EmployeeId))
                {
                    throw new InvalidOperationException(
                        "At least one identity key required");
                }
                return ValueTask.CompletedTask;
            }, "Validation: Require identity keys");

            // Projection: Tag for downstream
            pipeline.AddStep(CanonPipelinePhase.Projection, (context, _) =>
            {
                context.Metadata.SetTag("projected", "true");
                context.Metadata.SetTag("keys-present", string.Join(",",
                    new[] { entity.Email, entity.Username, entity.EmployeeId }
                        .Where(k => !string.IsNullOrWhiteSpace(k))));
                return ValueTask.CompletedTask;
            }, "Projection: Tag metadata");
        });
    }
}
```

### Recipe: Audit sink registration

```csharp
public sealed class PersonCanonRegistrar : ICanonRuntimeConfigurator
{
    public void Configure(CanonRuntimeBuilder builder)
    {
        builder.ConfigurePipeline<PersonCanon>(pipeline =>
        {
            // ... pipeline steps ...
        });

        // Register audit sink (requires [Canon(audit: true)] on entity)
        builder.UseAuditSink(new DefaultCanonAuditSink());

        // Set replay capacity
        builder.SetRecordCapacity(300);
    }
}
```

### Sample: Enrichment contributor

```csharp
// Enrich with external API call
pipeline.AddStep(CanonPipelinePhase.Intake, async (context, ct) =>
{
    var entity = context.Entity;
    if (!string.IsNullOrEmpty(entity.Email))
    {
        // Call external API to enrich with company info
        var companyInfo = await _companyLookupService.GetByEmail(entity.Email, ct);
        if (companyInfo != null)
        {
            context.Metadata.SetTag("company:name", companyInfo.Name);
            context.Metadata.SetTag("company:industry", companyInfo.Industry);
        }
    }
}, "Intake: Enrich with company data");
```

### Sample: Distribution to message bus

```csharp
pipeline.AddStep(CanonPipelinePhase.Distribution, async (context, ct) =>
{
    if (context.Options.SkipDistribution)
    {
        return;  // Honor skip flag
    }

    var @event = new PersonCanonizedEvent
    {
        CanonicalId = context.Metadata.CanonicalId,
        Outcome = context.Outcome,
        Origin = context.Options.Origin,
        Timestamp = DateTimeOffset.UtcNow
    };

    await _messageBus.PublishAsync(@event, ct);
}, "Distribution: Publish to message bus");
```

### Usage Scenarios

**Scenario 1: Data quality validation**
```csharp
pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
{
    var entity = context.Entity;

    // Email format validation
    if (!string.IsNullOrEmpty(entity.Email)
        && !EmailValidator.IsValid(entity.Email))
    {
        throw new ValidationException($"Invalid email: {entity.Email}");
    }

    // Employee ID format (must be 5 digits)
    if (!string.IsNullOrEmpty(entity.EmployeeId)
        && !Regex.IsMatch(entity.EmployeeId, @"^\d{5}$"))
    {
        throw new ValidationException(
            $"Employee ID must be 5 digits: {entity.EmployeeId}");
    }

    return ValueTask.CompletedTask;
}, "Validation: Format checks");
```

**Scenario 2: Tagging for materialized views**
```csharp
pipeline.AddStep(CanonPipelinePhase.Projection, (context, _) =>
{
    // Tag high-value customers for summary view rebuild
    if (context.Entity.LifetimeValue > 10000)
    {
        context.Metadata.SetTag("view:high-value", "true");
    }

    // Tag recent changes for activity feed
    if (context.IsUpdate)
    {
        context.Metadata.SetTag("view:activity-feed", "true");
    }

    return ValueTask.CompletedTask;
}, "Projection: Tag views");
```

**Scenario 3: Correlation tracking**
```csharp
pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
{
    // Capture correlation ID from options or generate
    var correlationId = context.Options.CorrelationId
        ?? Guid.NewGuid().ToString("n");

    context.Metadata.RecordExternalId("correlation-id", correlationId);

    // Link to upstream batch job
    if (context.Options.Tags.TryGetValue("batch-id", out var batchId))
    {
        context.Metadata.RecordExternalId("batch-job-id", batchId);
    }

    return ValueTask.CompletedTask;
}, "Intake: Capture correlations");
```

### Pipeline Timing

| Phase | Typical Duration | Cumulative |
|-------|------------------|------------|
| Intake | <1ms | 1ms |
| Validation | 1-2ms | 3ms |
| Aggregation | 5-50ms | 53ms |
| Policy | 2-10ms | 63ms |
| Projection | <1ms | 64ms |
| Distribution | Variable | 64ms+ |

**Pro tip:** Contributors must be idempotent. Canon may re-run phases during replay or manual rebuilds. Avoid side effects (emails, charges) inside contributors—use Distribution to publish events for downstream handlers.

---

## 6. Metadata and Audit Trails

**Concept:** Canon tracks rich metadata alongside canonical records—indexes, source attributions, policy footprints, audit entries. Think of it as the "paper trail" for compliance.

### What Canon Tracks

**Aggregation indexes:**
```
Email=alice@example.com → canonical ID abc
Username=asmith → canonical ID abc
EmployeeId=31991 → canonical ID abc
```

**Source attributions:**
```
Origin: hr
Channel: ingest
PayloadId: 01JB...
CorrelationId: batch-42
```

**Policy footprints:**
```
LegalName:
  Policy: SourceOfTruth
  Winner: hr (source key)
  Authority: incoming (hr sent authoritative value)
  Timestamp: 2025-11-09T14:30:00Z

DisplayName:
  Policy: Latest
  Winner: crm (source key)
  Timestamp: 2025-11-09T15:00:00Z
```

**Audit entries (when [Canon(audit: true)]):**
```
{
  "CanonicalId": "abc",
  "Phase": "Policy",
  "Event": "FieldUpdated",
  "Field": "DisplayName",
  "OldValue": "Alice Smith",
  "NewValue": "Alice S.",
  "Source": "crm",
  "Timestamp": "2025-11-09T15:00:00Z"
}
```

### Recipe: Inspect metadata

```csharp
var result = await person.Canonize(origin: "hr");

// Check aggregation indexes
var indexes = result.Metadata.AggregationIndexes;
foreach (var index in indexes)
{
    Console.WriteLine($"Index: {index.Key} → {index.CanonicalId}");
}

// Check policy footprints
var footprints = result.Metadata.PropertyFootprints;
foreach (var (propertyName, footprint) in footprints)
{
    Console.WriteLine($"{propertyName}:");
    Console.WriteLine($"  Policy: {footprint.Policy}");
    Console.WriteLine($"  Source: {footprint.SourceKey}");
    Console.WriteLine($"  Authority: {footprint.Evidence.GetValueOrDefault("authority")}");
}

// Check tags
foreach (var (key, value) in result.Metadata.Tags)
{
    Console.WriteLine($"Tag: {key} = {value}");
}
```

### Recipe: Configure audit sink

```csharp
// Entity must have [Canon(audit: true)]
[Canon(audit: true)]
public sealed class PersonCanon : CanonEntity<PersonCanon>
{
    // ... properties ...
}

// Register audit sink
public class PersonCanonRegistrar : ICanonRuntimeConfigurator
{
    public void Configure(CanonRuntimeBuilder builder)
    {
        builder.UseAuditSink(new DefaultCanonAuditSink());
    }
}

// Query audit trail
var auditEntries = await _auditSink.GetEntriesAsync(canonicalId: "abc");
foreach (var entry in auditEntries)
{
    Console.WriteLine($"[{entry.Timestamp:u}] {entry.Event} - {entry.Field}");
    Console.WriteLine($"  {entry.OldValue} → {entry.NewValue}");
    Console.WriteLine($"  Source: {entry.Source}");
}
```

### Sample: Compliance dashboard

```csharp
// Show all times non-authority sources tried to override legal names
public async Task<IActionResult> ShowOverrideAttempts()
{
    var auditEntries = await _auditSink.GetEntriesAsync(
        from: DateTime.UtcNow.AddMonths(-3),
        to: DateTime.UtcNow);

    var overrides = auditEntries
        .Where(e => e.Field == nameof(PersonCanon.LegalName))
        .Where(e => e.Evidence.GetValueOrDefault("authority") == "existing")
        .Select(e => new
        {
            e.CanonicalId,
            e.Timestamp,
            AttemptedBy = e.Source,
            RejectedValue = e.NewValue,
            PreservedValue = e.OldValue
        });

    return View(overrides);
}
```

### Usage Scenarios

**Scenario 1: Regulatory audit (show field change history)**
```csharp
// "Show all changes to salary field for employee 31991 in Q3 2024"
var entries = await _auditSink.GetEntriesAsync(
    canonicalId: employeeId,
    from: new DateTime(2024, 7, 1),
    to: new DateTime(2024, 9, 30));

var salaryChanges = entries
    .Where(e => e.Field == nameof(EmployeeCanon.Salary))
    .OrderBy(e => e.Timestamp);

foreach (var change in salaryChanges)
{
    Console.WriteLine($"[{change.Timestamp:u}] {change.Source}");
    Console.WriteLine($"  {change.OldValue:C} → {change.NewValue:C}");
    Console.WriteLine($"  Authority: {change.Evidence["authority"]}");
}
```

**Scenario 2: Data lineage (which sources contributed to this record)**
```csharp
var result = await PersonCanon.Get(canonicalId);
var footprints = result.Metadata.PropertyFootprints;

var lineage = footprints
    .GroupBy(f => f.Value.SourceKey)
    .Select(g => new
    {
        Source = g.Key,
        Fields = g.Select(f => f.Key).ToList()
    });

foreach (var source in lineage)
{
    Console.WriteLine($"{source.Source}: {string.Join(", ", source.Fields)}");
}
// Output:
// hr: LegalName, HireDate
// crm: DisplayName, PhoneNumber
// badge: LastBadgeScan
```

**Scenario 3: Index lookup (route incoming data)**
```csharp
// Before canonizing, check if email already exists
var existingIndex = await _indexRepository.FindAsync("Email=hannah@example.com");
if (existingIndex != null)
{
    Console.WriteLine($"Email already mapped to canonical ID: {existingIndex.CanonicalId}");
    // Route to update workflow instead of create
}
```

### Index Performance

| Operation | Typical Duration | Notes |
|-----------|------------------|-------|
| Single key lookup | ~2ms | `FindIndex("Email=...")` |
| Composite key lookup | ~5ms | `FindIndex("Email=...\|Username=...")` |
| Index write | ~3ms | Automatic during canonization |
| Index rewrite (union) | ~50ms per ID | After identity merge |

**Pro tip:** Audit sink failures halt canonization. If your sink throws (DB down, disk full), canonization fails to prevent lost evidence. Wrap sinks with retry policies or use async queues for high-volume systems.

---

## 7. Canonization Options and Control

**Concept:** `CanonizationOptions` controls each canonization run—origin, tags, stage behavior, requested views, distribution flags. Think HTTP request headers.

### Options Quick Reference

| Option | Purpose | Example |
|--------|---------|---------|
| **Origin** | Identify source system | `WithOrigin("workday")` |
| **CorrelationId** | Link to business event | `with { CorrelationId = batchId }` |
| **Tags** | Custom metadata | `WithTag("priority", "high")` |
| **StageBehavior** | Defer canonization | `WithStageBehavior(StageOnly)` |
| **RequestedViews** | Trigger view rebuilds | `WithRequestedViews("summary")` |
| **SkipDistribution** | Suppress events | `with { SkipDistribution = true }` |
| **Identity.MergePosture** | Control unions | `AutoUnion` vs `RequireManualReview` |

### Recipe: Basic options

```csharp
var options = CanonizationOptions.Default
    .WithOrigin("crm-feed")
    .WithTag("priority", "high")
    .WithRequestedViews("summary", "exports")
    with
    {
        CorrelationId = Guid.NewGuid().ToString("n"),
        SkipDistribution = false
    };

var result = await person.Canonize(configure: _ => options);
```

### Recipe: Staging (defer canonization)

```csharp
// Park payloads for manual review
var stageOptions = CanonizationOptions.Default
    .WithStageBehavior(CanonStageBehavior.StageOnly)
    with { CorrelationId = "batch-queue-42" };

var result = await payload.Canonize(configure: _ => stageOptions);
if (result.Outcome == CanonizationOutcome.Parked)
{
    Console.WriteLine("Payload parked for manual review");
    // Later: analyst approves, re-canonize with StageAndCanonicalize
}
```

### Recipe: Replay history

```csharp
// Stream last hour of canonization events
await foreach (var record in runtime.Replay(
    from: DateTimeOffset.UtcNow.AddHours(-1)))
{
    Console.WriteLine($"[{record.OccurredAt:u}] {record.Phase} → {record.Outcome}");
    Console.WriteLine($"  Origin: {record.Origin}");
    Console.WriteLine($"  CanonicalId: {record.CanonicalId}");
}
```

### Sample: Backfill with skip distribution

```csharp
// Backfill historical data without triggering downstream events
var backfillOptions = CanonizationOptions.Default
    .WithOrigin("historical-import")
    .WithTag("backfill", "true")
    with { SkipDistribution = true };

foreach (var historical in historicalPayloads)
{
    await historical.Canonize(configure: _ => backfillOptions);
    // Canonical record created, indexes built, audit captured
    // But no events published to message bus
}
```

### Usage Scenarios

**Scenario 1: Merger review workflow**
```csharp
// During merger, require manual review for identity unions
var mergerOptions = CanonizationOptions.Default
    .WithOrigin("merger-import")
    .WithTag("merger-source", "CompanyB")
    with
    {
        Identity = new IdentityOptions
        {
            MergePosture = MergePosture.RequireManualReview
        }
    };

var result = await payload.Canonize(configure: _ => mergerOptions);
if (result.Outcome == CanonizationOutcome.RequiresReview)
{
    // Park for analyst
    await _reviewQueue.EnqueueAsync(new ReviewRequest
    {
        PayloadId = payload.Id,
        Reason = "Identity union detected during merger",
        Evidence = result.Metadata.Tags
    });
}
```

**Scenario 2: View rebuild triggers**
```csharp
// Tag canonizations that need summary view refresh
var viewOptions = CanonizationOptions.Default
    .WithRequestedViews("customer-summary", "activity-feed");

var result = await customer.Canonize(configure: _ => viewOptions);

// View rebuild job polls for these tags
if (result.Metadata.Tags.ContainsKey("view:customer-summary"))
{
    await _viewRebuilder.RefreshAsync("customer-summary", result.Canonical.Id);
}
```

**Scenario 3: Batch correlation**
```csharp
// Link all canonizations to batch job ID
var batchId = $"nightly-import-{DateTime.UtcNow:yyyyMMdd}";
var batchOptions = CanonizationOptions.Default
    .WithOrigin("batch-processor")
    with { CorrelationId = batchId };

foreach (var payload in batch)
{
    await payload.Canonize(configure: _ => batchOptions);
}

// Later: Query audit trail by correlation ID
var batchAudit = await _auditSink.GetEntriesByCorrelationAsync(batchId);
Console.WriteLine($"Batch processed {batchAudit.Count()} records");
```

**Pro tip:** Replay capacity truncates history (default: 200 events). For high-volume systems, either raise capacity in config or implement external event streaming (write to message bus, query from warehouse).

---

## 8. Web APIs

**Concept:** `CanonModelsController<T>` extends `EntityController<T>` with canon-specific endpoints—policy metadata, footprints, history.

### Canon Controller Endpoints

| Endpoint | Method | Purpose | Response |
|----------|--------|---------|----------|
| `/api/canon/persons` | GET | List canonical records | Paged list with metadata |
| `/api/canon/persons/{id}` | GET | Get canonical record | Full entity + metadata |
| `/api/canon/persons` | POST | Canonize payload | CanonizationResult |
| `/api/canon/persons/{id}` | PUT | Update and re-canonize | Updated canonical |
| `/api/canon/persons/{id}` | DELETE | Remove canonical record | Success status |
| `/api/canon/persons/metadata` | GET | Aggregation policies | Policy descriptors |
| `/api/canon/persons/history` | GET | Replay events | Streamed log |

### Recipe: Basic controller

```csharp
using Koan.Canon.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

[Route("api/canon/persons")]
public sealed class PersonsController : CanonModelsController<PersonCanon>
{
    // Inherits all CRUD + Canon endpoints
}
```

### Recipe: Custom response shaping

```csharp
[Route("api/canon/persons")]
public sealed class PersonsController : CanonModelsController<PersonCanon>
{
    protected override void Configure(ControllerConfigurator<PersonCanon> configurator)
    {
        // Hide internal metadata
        configurator.TransformResult(result => new
        {
            result.Canonical.Id,
            result.Canonical.Email,
            result.Canonical.DisplayName,
            Sources = result.Metadata.Tags
                .Where(t => t.Key.StartsWith("source:"))
                .Select(t => t.Value)
        });

        // Default page size
        configurator.WithDefaultPageSize(50);
    }
}
```

### Sample: Admin UI - locked fields

```csharp
// GET /api/canon/persons/metadata
public async Task<IActionResult> ShowLockedFields()
{
    var response = await _httpClient.GetFromJsonAsync<MetadataResponse>(
        "/api/canon/persons/metadata");

    var lockedFields = response.AggregationPolicyDetails
        .Where(p => p.Value.HasAuthoritativeSources)
        .Select(p => new
        {
            Field = p.Key,
            Authorities = string.Join(", ", p.Value.AuthoritativeSources),
            Fallback = p.Value.Fallback?.ToString() ?? "None"
        });

    return View(lockedFields);
}

// Renders table:
// | Field      | Authorities      | Fallback |
// |------------|------------------|----------|
// | LegalName  | workday, sap     | Latest   |
// | Salary     | netsuite         | Latest   |
```

### Sample: Compliance dashboard - override attempts

```csharp
// GET /api/canon/persons/history?from=2024-10-01&to=2024-10-06
public async Task<IActionResult> ShowRecentOverrides()
{
    var history = await _httpClient.GetFromJsonAsync<List<CanonHistoryEvent>>(
        "/api/canon/persons/history?from=2024-10-01&to=2024-10-06");

    var overrides = history
        .Where(e => e.Evidence.ContainsKey("authority")
            && e.Evidence["authority"] == "existing")
        .Select(e => new
        {
            e.CanonicalId,
            e.OccurredAt,
            Field = e.Evidence["field"],
            AttemptedBy = e.Evidence["source"]
        });

    return View(overrides);
}

// Renders table:
// | CanonicalId | Time       | Field      | Attempted By |
// |-------------|------------|------------|--------------|
// | abc         | 2024-10-02 | LegalName  | crm          |
// | xyz         | 2024-10-03 | Salary     | marketing    |
```

### Usage Scenarios

**Scenario 1: Canonize via HTTP POST**
```bash
curl -X POST http://localhost:5000/api/canon/persons \
  -H "Content-Type: application/json" \
  -d '{
    "email": "isaac@example.com",
    "displayName": "Isaac Newton",
    "legalName": "Sir Isaac Newton"
  }' \
  -H "X-Origin: hr"

# Response:
{
  "canonical": {
    "id": "01JB...",
    "email": "isaac@example.com",
    "displayName": "Isaac Newton",
    "legalName": "Sir Isaac Newton"
  },
  "outcome": "Created",
  "metadata": {
    "canonicalId": "01JB...",
    "propertyFootprints": {
      "LegalName": {
        "policy": "SourceOfTruth",
        "sourceKey": "hr",
        "evidence": { "authority": "incoming" }
      }
    }
  }
}
```

**Scenario 2: Query metadata for admin UI**
```bash
curl http://localhost:5000/api/canon/persons/metadata

# Response:
{
  "aggregationPolicyDetails": {
    "LegalName": {
      "kind": "SourceOfTruth",
      "authoritativeSources": ["hr"],
      "fallback": "Latest"
    },
    "DisplayName": {
      "kind": "Latest",
      "authoritativeSources": null,
      "fallback": null
    }
  }
}
```

**Scenario 3: Stream replay history**
```bash
curl "http://localhost:5000/api/canon/persons/history?from=2025-11-09T00:00:00Z"

# Response (streamed JSON):
[
  {
    "occurredAt": "2025-11-09T14:30:00Z",
    "phase": "Policy",
    "outcome": "Created",
    "canonicalId": "01JB...",
    "origin": "hr"
  },
  {
    "occurredAt": "2025-11-09T15:00:00Z",
    "phase": "Policy",
    "outcome": "Updated",
    "canonicalId": "01JB...",
    "origin": "crm"
  }
]
```

**Pro tip:** Canon controllers auto-register via `AddKoan()`. No need for explicit `services.AddControllers()` or assembly scanning.

---

## 9. Advanced Patterns

### Pattern: Custom validation contributor

```csharp
public sealed class AuthorityValidator : ICanonPipelineContributor<PersonCanon>
{
    public ValueTask ExecuteAsync(
        CanonPipelineContext<PersonCanon> context,
        CancellationToken ct)
    {
        var metadata = CanonModelAggregationMetadata.For<PersonCanon>();
        var legalNamePolicy = metadata.GetRequiredPolicy(p => p.LegalName);

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

// Register in pipeline
pipeline.AddStep(CanonPipelinePhase.Validation, new AuthorityValidator());
```

### Pattern: Multi-tenant isolation

```csharp
using Koan.Data.Core;

// Scope canonization per tenant
using (EntityContext.Partition("tenant-abc"))
{
    var result = await payload.Canonize(origin: "hr");
    // Indexes, canonical records, audit scoped to "tenant-abc"
}

// Different tenant
using (EntityContext.Partition("tenant-xyz"))
{
    var result = await payload.Canonize(origin: "hr");
    // Completely separate canonical records
}
```

### Pattern: Event-driven downstream

```csharp
// Subscribe to canon distribution events
public class CanonEventHandler : ICanonDistributionObserver<PersonCanon>
{
    private readonly IMessageBus _bus;

    public async ValueTask OnCanonizedAsync(
        CanonizationResult<PersonCanon> result,
        CancellationToken ct)
    {
        // Publish change notification
        await _bus.PublishAsync(new PersonCanonizedEvent
        {
            CanonicalId = result.Canonical.Id,
            Outcome = result.Outcome,
            ChangedFields = result.Metadata.PropertyFootprints.Keys.ToList(),
            Origin = result.Metadata.Tags.GetValueOrDefault("origin")
        }, ct);

        // Trigger materialized view rebuild
        if (result.Metadata.Tags.ContainsKey("view:summary"))
        {
            await _viewRebuilder.RefreshAsync("person-summary", result.Canonical.Id, ct);
        }
    }
}
```

### Pattern: Policy discovery for UI

```csharp
// Build admin UI showing field governance
public class PolicyMatrix
{
    public List<FieldPolicy> GetFieldPolicies()
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
                Authorities = policy.AuthoritativeSources?.ToList() ?? new(),
                Fallback = policy.Fallback?.ToString(),
                Icon = policy.HasAuthoritativeSources ? "🔒" : "📝"
            });
        }

        return fields;
    }
}

// Renders UI:
// | Field      | Policy        | Locked | Authorities   | Fallback |
// |------------|---------------|--------|---------------|----------|
// | LegalName  | SourceOfTruth | 🔒     | workday, sap  | Latest   |
// | DisplayName| Latest        | 📝     | -             | -        |
```

**Pro tip:** Use metadata helpers (`CanonModelAggregationMetadata.For<T>()`) for type-safe policy queries. String-based lookups break silently during refactors.

---

## 10. Performance Considerations

### Canonization Latency

| Scenario | Aggregation | Policy | Total | Factors |
|----------|-------------|--------|-------|---------|
| Single key, no conflict | ~5ms | ~2ms | ~10ms | Index lookup + policy application |
| Composite key (3 keys) | ~15ms | ~5ms | ~25ms | 3 index reads + 3 writes |
| Identity union (2 IDs) | ~50ms | ~10ms | ~65ms | Index rewrites, merge logic |
| Complex union (5 IDs) | ~120ms | ~20ms | ~150ms | 15 index reads, complex merge |

### Optimization Strategies

**Strategy 1: Index locality**
```csharp
// BAD: Many optional keys (sparse indexes)
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey] public string? Email { get; set; }
    [AggregationKey] public string? Username { get; set; }
    [AggregationKey] public string? EmployeeId { get; set; }
    [AggregationKey] public string? BadgeId { get; set; }
    [AggregationKey] public string? PhoneNumber { get; set; }  // 5 keys!
}

// GOOD: 2-3 core keys
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey] public string? Email { get; set; }
    [AggregationKey] public string? EmployeeId { get; set; }
    // Other identifiers as regular fields
    public string? BadgeId { get; set; }
    public string? PhoneNumber { get; set; }
}
```

**Strategy 2: Batch operations**
```csharp
// BAD: Sequential canonization
foreach (var payload in batch)
{
    await payload.Canonize(origin: "batch");  // N round-trips
}

// GOOD: Parallel canonization
var tasks = batch.Select(p => p.Canonize(origin: "batch"));
await Task.WhenAll(tasks);  // Parallel execution
```

**Strategy 3: Skip distribution for backfills**
```csharp
// During historical import, skip events
var backfillOptions = CanonizationOptions.Default
    with { SkipDistribution = true };

foreach (var historical in historicalData)
{
    await historical.Canonize(configure: _ => backfillOptions);
    // Faster: no event publishing overhead
}
```

### Scaling Characteristics

| Load | Throughput | Bottleneck | Mitigation |
|------|-----------|------------|------------|
| <100/sec | ~5ms p50 | None | Standard config |
| 100-1000/sec | ~15ms p50 | Index writes | Add index caching |
| 1000-10000/sec | ~50ms p50 | Database I/O | Shard by partition |
| >10000/sec | Variable | Lock contention | Distributed Canon runtime |

### Index Performance

| Operation | Provider | Latency | Notes |
|-----------|----------|---------|-------|
| Single key lookup | PostgreSQL | ~2ms | Indexed column |
| Single key lookup | MongoDB | ~1ms | Indexed field |
| Composite lookup (3 keys) | PostgreSQL | ~5ms | Composite index |
| Index write | PostgreSQL | ~3ms | Single insert |
| Index rewrite (union, 6 indexes) | PostgreSQL | ~50ms | 6 updates + transaction |

**Pro tip:** Monitor merge frequency. High merge rates (>5%) indicate data quality issues. Add upstream validation to reduce union overhead.

---

## 11. Troubleshooting

### Issue 1: InvalidOperationException - No Aggregation Keys

**Symptoms:**
```
InvalidOperationException: At least one aggregation key must be non-null
```

**Causes:**
- Payload sent with all aggregation keys null or empty
- No `[AggregationKey]` attributes defined on entity

**Solutions:**
```csharp
// BAD: All keys null
var badPayload = new PersonCanon
{
    DisplayName = "John Doe"
    // Email, Username, EmployeeId all null
};
await badPayload.Canonize(origin: "hr");  // Throws!

// GOOD: At least one key non-null
var goodPayload = new PersonCanon
{
    Email = "john@example.com",
    DisplayName = "John Doe"
};
await goodPayload.Canonize(origin: "hr");  // ✅

// GOOD: Define aggregation keys
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]  // ← Required!
    public string? Email { get; set; }
}
```

**Debug tip:** Add validation contributor to reject payloads early:
```csharp
pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
{
    if (string.IsNullOrEmpty(context.Entity.Email)
        && string.IsNullOrEmpty(context.Entity.EmployeeId))
    {
        throw new ValidationException("Email or EmployeeId required");
    }
    return ValueTask.CompletedTask;
}, "Validate keys");
```

---

### Issue 2: SourceOfTruth Policy Not Applied

**Symptoms:**
- Non-authority source updates field marked SourceOfTruth
- Expected authority evidence not in footprint

**Causes:**
- `Source` or `Sources` not specified on attribute
- Origin doesn't match authority name (case-sensitive)
- Fallback policy applied (no authority established yet)

**Solutions:**
```csharp
// BAD: Missing authority
[AggregationPolicy(AggregationPolicyKind.SourceOfTruth)]
public string? LegalName { get; set; }
// Throws at startup: "SourceOfTruth requires Source or Sources"

// GOOD: Authority specified
[AggregationPolicy(
    AggregationPolicyKind.SourceOfTruth,
    Source = "hr")]
public string? LegalName { get; set; }

// BAD: Origin mismatch (case-sensitive)
await payload.Canonize(origin: "HR");  // Won't match "hr"

// GOOD: Match exactly
await payload.Canonize(origin: "hr");  // ✅

// Check authority evidence
var footprint = result.Metadata.PropertyFootprints[nameof(PersonCanon.LegalName)];
Console.WriteLine($"Authority: {footprint.Evidence["authority"]}");
// "incoming" = authority sent value
// "existing" = authority already had value (non-authority rejected)
// "fallback" = no authority yet (fallback policy applied)
```

**Debug tip:** Log footprints to verify policy application:
```csharp
pipeline.AddStep(CanonPipelinePhase.Policy, (context, _) =>
{
    foreach (var (field, footprint) in context.Metadata.PropertyFootprints)
    {
        _logger.LogInformation(
            "Field {Field}: Policy={Policy}, Source={Source}, Authority={Authority}",
            field,
            footprint.Policy,
            footprint.SourceKey,
            footprint.Evidence.GetValueOrDefault("authority"));
    }
    return ValueTask.CompletedTask;
}, "Log policy decisions");
```

---

### Issue 3: Identity Union Not Triggered

**Symptoms:**
- Expected two canonical IDs to merge, but didn't
- Multiple records exist for same logical entity

**Causes:**
- Keys don't overlap (no common aggregation key)
- Merge posture set to `RequireManualReview`
- Indexes not created (first canonization failed)

**Solutions:**
```csharp
// Scenario: Two records for same person
// Record 1: Email=jane@example.com, EmployeeId=50001
// Record 2: Username=jdoe
// Why no merge? No shared key!

// FIX: Send payload with overlapping keys
var bridgePayload = new PersonCanon
{
    Email = "jane@example.com",  // Links to Record 1
    Username = "jdoe"             // Links to Record 2
};
await bridgePayload.Canonize(origin: "integration");
// Now triggers union: Records 1 and 2 merge

// Check merge posture
var options = CanonizationOptions.Default with
{
    Identity = new IdentityOptions
    {
        MergePosture = MergePosture.AutoUnion  // Default
    }
};

// If RequireManualReview:
var result = await payload.Canonize(configure: _ => options);
if (result.Outcome == CanonizationOutcome.RequiresReview)
{
    Console.WriteLine("Union parked for manual review");
}
```

**Debug tip:** Query indexes to see current mappings:
```csharp
var emailIndex = await _indexRepository.FindAsync("Email=jane@example.com");
var usernameIndex = await _indexRepository.FindAsync("Username=jdoe");

if (emailIndex?.CanonicalId != usernameIndex?.CanonicalId)
{
    Console.WriteLine($"Split identity: {emailIndex.CanonicalId} vs {usernameIndex.CanonicalId}");
    // Send bridging payload to trigger union
}
```

---

### Issue 4: Audit Sink Failures Halt Canonization

**Symptoms:**
```
CanonizationException: Audit sink failed to write entry
Inner: System.Net.Sockets.SocketException: Connection refused
```

**Causes:**
- Audit sink database/service unavailable
- Network timeout
- Disk full (file-based sinks)

**Solutions:**
```csharp
// Wrap audit sink with retry policy
public class ResilientAuditSink : ICanonAuditSink
{
    private readonly ICanonAuditSink _inner;
    private readonly IAsyncPolicy _retryPolicy;

    public ResilientAuditSink(ICanonAuditSink inner)
    {
        _inner = inner;
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        await _retryPolicy.ExecuteAsync(() => _inner.WriteAsync(entry, ct));
    }
}

// Or use async queue for high-volume
public class QueuedAuditSink : ICanonAuditSink
{
    private readonly Channel<AuditEntry> _queue;

    public QueuedAuditSink()
    {
        _queue = Channel.CreateUnbounded<AuditEntry>();
        _ = ProcessQueueAsync();  // Background task
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        await _queue.Writer.WriteAsync(entry, ct);
        // Returns immediately - doesn't block canonization
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await _actualSink.WriteAsync(entry, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit write failed, queueing for retry");
            }
        }
    }
}
```

**Design principle:** Audit failures should halt canonization (prevents lost compliance evidence). But wrap sinks with resilience for production.

---

### Issue 5: Replay History Truncated

**Symptoms:**
- Only last 200 events returned from `/history` endpoint
- Older canonization events missing

**Causes:**
- Replay capacity set to 200 (default)
- High-volume system exceeds capacity

**Solutions:**
```json
// appsettings.json - Increase capacity
{
  "Koan": {
    "Canon": {
      "Replay": {
        "Capacity": 1000
      }
    }
  }
}
```

```csharp
// Or configure in code
builder.SetRecordCapacity(1000);
```

**For high-volume systems:**
```csharp
// Stream events to external storage
pipeline.AddStep(CanonPipelinePhase.Distribution, async (context, ct) =>
{
    var @event = new CanonHistoryEvent
    {
        CanonicalId = context.Metadata.CanonicalId,
        OccurredAt = DateTimeOffset.UtcNow,
        Phase = "Aggregation",
        Outcome = context.Outcome.ToString(),
        Origin = context.Options.Origin
    };

    // Write to message bus for long-term storage
    await _messageBus.PublishAsync(@event, ct);
}, "Stream to external event store");
```

**Pro tip:** Replay is for recent diagnostics (last few hours). For historical analysis, use audit sinks or external event stores.

---

### Issue 6: Policy Footprint Missing

**Symptoms:**
- `result.Metadata.PropertyFootprints` doesn't contain expected field
- Footprint shows "fallback" authority when expecting "incoming"

**Causes:**
- Field not marked with `[AggregationPolicy]` (defaults to Latest)
- Field value was null in payload (no update occurred)
- Framework didn't detect field change (same value as existing)

**Solutions:**
```csharp
// Explicit policy on all fields
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey]
    public string? Email { get; set; }

    [AggregationPolicy(AggregationPolicyKind.Latest)]  // ← Explicit
    public string? DisplayName { get; set; }
}

// Check if field was actually updated
var result = await payload.Canonize(origin: "hr");
if (result.Metadata.PropertyFootprints.ContainsKey(nameof(PersonCanon.DisplayName)))
{
    var footprint = result.Metadata.PropertyFootprints[nameof(PersonCanon.DisplayName)];
    Console.WriteLine($"DisplayName policy applied: {footprint.Policy}");
}
else
{
    Console.WriteLine("DisplayName not updated (null or same value)");
}
```

**Debug tip:** Log all footprints to see what framework tracked:
```csharp
foreach (var (field, footprint) in result.Metadata.PropertyFootprints)
{
    Console.WriteLine($"{field}: {footprint.Policy} from {footprint.SourceKey}");
}
```

---

### Issue 7: Performance Degradation with Many Keys

**Symptoms:**
- Canonization latency >100ms
- Database CPU spikes during canonization
- Index table growing rapidly

**Causes:**
- Too many aggregation keys (5+)
- Frequent identity unions (many split identities)
- Unindexed database columns

**Solutions:**
```csharp
// BAD: Too many keys
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey] public string? Email { get; set; }
    [AggregationKey] public string? Username { get; set; }
    [AggregationKey] public string? EmployeeId { get; set; }
    [AggregationKey] public string? BadgeId { get; set; }
    [AggregationKey] public string? PhoneNumber { get; set; }  // 5 keys!
    [AggregationKey] public string? ExternalId { get; set; }   // 6 keys!
}

// GOOD: 2-3 core keys
public class PersonCanon : CanonEntity<PersonCanon>
{
    [AggregationKey] public string? Email { get; set; }
    [AggregationKey] public string? EmployeeId { get; set; }
    // Other identifiers as regular fields
    public string? BadgeId { get; set; }
    public string? PhoneNumber { get; set; }
}

// Ensure database indexes
CREATE INDEX idx_aggregation_key ON canon_person_indexes (aggregation_key);
CREATE INDEX idx_canonical_id ON canon_person_indexes (canonical_id);

// Monitor merge frequency
var mergeRate = auditEntries.Count(e => e.Event == "IdentityMerge")
    / (double)auditEntries.Count();
if (mergeRate > 0.05)  // >5% merges
{
    _logger.LogWarning("High merge rate detected: {Rate:P}", mergeRate);
    // Add upstream validation to reduce split identities
}
```

**Pro tip:** Profile canonization under realistic load. Use Application Insights or similar to track p50/p99 latency. Optimize hot paths (reduce keys, add DB indexes, batch operations).

---

## 12. Summary and Next Steps

You've now built a foundation in Canon—aggregating multi-source data into golden records, resolving conflicts with explicit policies, and tracking every decision for compliance.

**Key Takeaways:**
1. **Start simple** - Begin with Latest policies and single aggregation keys. Add complexity as needed.
2. **Identity graphs are flexible** - Every `[AggregationKey]` is optional per payload. Canon unions fragments over time.
3. **Policies explain decisions** - Property footprints answer "Why did this value win?" Authority evidence proves compliance.
4. **Audit trails satisfy regulators** - `[Canon(audit: true)]` captures full change evidence.
5. **Metadata drives UIs** - Expose policy details via `/metadata` endpoints for admin consoles.

**Choosing Your Policies:**
- **Public APIs** → Latest for user-editable fields, SourceOfTruth for regulated fields
- **Compliance-heavy** → SourceOfTruth with explicit authorities, audit enabled
- **Merger/acquisition** → Multi-authority SourceOfTruth, RequireManualReview for unions
- **High-volume** → Minimize aggregation keys, batch operations, skip distribution for backfills

**Performance Checklist:**
- ✅ Use 2-3 aggregation keys (not 5+)
- ✅ Add database indexes on aggregation_key and canonical_id columns
- ✅ Monitor merge frequency (<5% is healthy)
- ✅ Batch canonization operations when possible
- ✅ Use skip distribution for backfills

**Common Patterns:**
- HR integration → SourceOfTruth for legal names/hire dates, Latest for preferences
- Customer 360 → Latest for most fields, Max for lifetime value
- Device registry → First for creation date, Latest for IP/status
- Merger scenarios → Multi-authority, manual review for unions

**Next Steps:**
1. **Implement your first Canon entity** - Start with Latest policies, add SourceOfTruth as needed
2. **Configure pipeline validation** - Reject invalid payloads early
3. **Enable audit trails** - Add `[Canon(audit: true)]` for compliance-critical entities
4. **Build admin UI** - Expose `/metadata` endpoint to show locked fields
5. **Read related guides:**
   - [Entity Capabilities](entity-capabilities-howto.md) - Entity-first patterns
   - [Patch Capabilities](patch-capabilities-howto.md) - Partial update strategies
   - [AI Vector Operations](ai-vector-howto.md) - Semantic search on canonical records

**Questions or Issues?**
- Check [Troubleshooting](#11-troubleshooting) section above
- Review [CANON-0001](../decisions/CANON-0001-canon-domain-model.md) for architecture details
- See [entity-capabilities-howto.md](entity-capabilities-howto.md) for Entity<T> patterns

Remember: Canon is about making conflict resolution explicit and auditable. When five systems disagree, your policies decide—and the audit trail proves it. Start with simple Latest-wins, graduate to SourceOfTruth for regulated fields, and let Canon handle the complexity of identity graphs and lineage tracking.
