---
type: ARCHITECTURE
domain: data
title: "DATA-0110 - Compact provider-neutral Data adapter language"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: reviewed
  scope: public mapping and registered-operation grammar
---

# DATA-0110 — Compact provider-neutral Data adapter language

## Context

Koan needs to map typed aggregates onto existing relational, document, key/value, search, and hybrid physical shapes
without becoming a relationship or unit-of-work ORM. The first proposed fluent surface repeated context and exposed
family-specific vocabulary:

```csharp
.Field(customer => customer.Name.Full).Column("DISPLAY_NM")
.Field(customer => customer.Profile).JsonColumn("PROFILE_JSON")
```

Registered reads similarly repeated source or result context through names such as `NamedQuery`, `ReadLane`, and
`SearchTemplate`. Those names made the common application language longer while coupling it to one physical model.

## Decision

A fluent method adds one new decision and never repeats context already established by the chain.

The mapping grammar is:

- logical selection: `Key`, `Property`, and root `Object`;
- physical location: `Container`, `Name`, and `Path`; and
- explicit behavior: `Generated`, `ReadOnly`, and `Codec`.

```csharp
koan.Data.Source("LegacyErp").Map<Customer>(map => map
    .Container("dbo", "CUSTOMER")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
    .Property(customer => customer.Profile).Object("PROFILE_JSON"));
```

`Object` means one logical subtree represented by one physical structured value. It does not mean a relational JSON
column. A provider or Family realizes that shape natively; an explicit codec handles a non-canonical legacy encoding.
`Path` carries provider-neutral physical segments rather than a provider query-path language. A `StorageAddress`
overload remains for programmatic composition, while common literal container segments require no wrapper.

Registered read shape is selected at entry:

```csharp
koan.Data.Source("ProductSearch").Query(
    "products.low-stock",
    query => query
        .Template("products-low-stock-v2")
        .Parameter<int>("threshold")
        .MaxRecords(100));

koan.Data.Source("LegacyErp").Scalar<long>(
    "orders.recent-count",
    query => query
        .Lane("Reports")
        .Sql("select count(*) from dbo.ORDERS")
        .MaxValueBytes(64));
```

The common public verbs are `Query`, `Scalar`, and `Lane`. Provider binding leaves name the native artifact honestly
without repeating the provider context: `Sql`, `Pipeline`, `Template`, or `Function`. Safety and capability words such
as `MaxRecords`, `MaxBytes`, timeout, `Generated`, and `ReadOnly` remain explicit because they add a distinct semantic
axis.

All fluent forms compile into one immutable neutral descriptor containing:

```text
logical path
role                 key | property | whole object
physical location    name | path
shape                scalar | object
codec, direction, generation, and authority modifiers
```

Adapters consume the compiled plan. They do not implement or reinterpret the fluent grammar.
For a canonical whole-object binding, expanded logical paths are derived query/projection metadata rather than
independent hydration authority. A getter-only computed member may therefore remain addressable without acquiring a
setter; canonical property bindings still require a writable hydration path.

## Consequences

- Application examples read in model terms rather than relational or document dialect.
- IntelliSense exposes only valid next decisions through typed builder states.
- Common literal cases are short; advanced descriptor and codec overloads remain available without entering the
  ordinary path.
- `Field` remains valid terminology inside neutral `RecordSet` shape metadata, where values are not necessarily CLR
  properties and duplicate names are legal. It is not the typed mapping selector.
- Provider-specific terminology remains appropriate for native probes, plans, diagnostics, and binding leaves.
- The exact API is guarded by consumer compile specifications before adapter implementation begins.
- Adapter conformance is proved by ordinary shared contract tests and focused real-provider tests. The primer remains
  authoring guidance; Koan does not maintain a parallel certification catalog, evidence packet protocol, source-hash
  manifest, or project-status ledger that can disagree with executable behavior.

## Related

- [Koan Data Adapter Development Primer](../architecture/data-adapter-development-primer.md)
- [DATA-0017 storage naming conventions](DATA-0017-storage-naming-conventions.md)
- [DATA-0098 identity encoding codec](DATA-0098-identity-encoding-codec.md)
