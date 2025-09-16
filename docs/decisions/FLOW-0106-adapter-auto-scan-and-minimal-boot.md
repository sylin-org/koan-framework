---
id: FLOW-0106
slug: adapter-auto-scan-and-minimal-boot
domain: Flow
status: accepted
date: 2025-09-02
title: Flow adapter auto-scan and minimal boot (Core host binder + adapter auto-registration)
---

## Contract (at a glance)

- Inputs: Assembly types annotated with [FlowAdapter] and deriving from BackgroundService; environment/config via `Koan:Flow:Adapters:*`.
- Outputs: Adapter BackgroundServices registered automatically in DI; AppHost and KoanEnv are initialized early for generic hosts.
- Error modes: None by default; misconfiguration may skip registration (e.g., excluded or AutoStart=false). Clear log messages recommended.
- Success: Minimal Program.cs per host: `AddKoan()` (and `AddKoanFlow()` for web) with no manual messaging/host wiring.

## Decision

We enable minimal boot for Flow adapter and API hosts by:

1) Core generic-host binder
   - A small hosted service binds `AppHost.Current` and calls `KoanEnv.TryInitialize(sp)` early in generic-host apps (non-web). This removes the need for manual ambient runtime wiring in Program.cs.

2) Flow adapter auto-registration
   - The Flow AutoRegistrar discovers non-abstract `BackgroundService` types annotated with `[FlowAdapter]` in loaded assemblies and registers them as `IHostedService`.
   - Discovery is gated by config and environment with sane defaults:
     - `Koan:Flow:Adapters:AutoStart` (bool) — default: true when running in containers, false otherwise.
     - `Koan:Flow:Adapters:Include` (string[]) — optional whitelist using `"system:adapter"` identifiers.
     - `Koan:Flow:Adapters:Exclude` (string[]) — optional blacklist using `"system:adapter"` identifiers.
   - The registrar also wires `IFlowIdentityStamper` so adapters don’t need to register it manually.

Consequently, adapter and API hosts only need to reference Koan modules and call `AddKoan()` (plus `AddKoanFlow()` for web APIs). Messaging and RabbitMQ wiring remain self-registered by their modules.

## Rationale

- Aligns with Koan’s “reference = intent” and auto-registration posture (DX-0038), reducing repeated boilerplate across hosts.
- Centralizes magic values and options under `Koan:Flow:*` (ARCH-0040) and respects environment defaults (OPS-0015).
- Keeps behavior discoverable and overridable via config.

## Configuration

Example appsettings.json:

{
  "Koan": {
    "Flow": {
      "Adapters": {
        "AutoStart": true,
        "Include": [ "oem:publisher" ],
        "Exclude": [ "bms:simulator" ]
      }
    }
  }
}

Notes
- Omit Include/Exclude for default all-on behavior (subject to AutoStart).
- In non-container environments, set `AutoStart: true` explicitly to opt-in.

## Minimal Program patterns

Adapter host (generic host):

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();
await builder.Build().RunAsync();

Web API host:

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddKoanFlow();
var app = builder.Build();
app.UseKoan();
app.Run();

## Edge cases

- No IConfiguration available: registrar falls back to environment defaults; no crash.
- Multiple adapter types with same `system:adapter`: include/exclude lists match by metadata, not type name.
- Performance: scan limited to loaded assemblies at startup; negligible impact for typical apps.
- Explicit registration: manual `AddHostedService<T>()` still works; duplicates are avoided by enumerable registration.

## Consequences

- S8 sample hosts are simplified; explicit messaging and identity-stamper wiring removed.
- Containerized runs gain sensible defaults (AutoStart=true) while local dev stays explicit unless configured.

## References

- ARCH-0039 — KoanEnv static runtime
- ARCH-0040 — Config and constants naming
- DX-0038 — Auto-registration
- FLOW-0103 — DX toolkit and adapter metadata
- FLOW-0105 — External ID translation and adapter identity
