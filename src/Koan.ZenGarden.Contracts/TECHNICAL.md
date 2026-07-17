---
uid: reference.modules.Koan.zengarden.contracts
title: Koan.ZenGarden.Contracts - Technical Reference
description: Inert client, discovery, connection-intent, and capability contracts for Zen Garden integrations.
packages: [Sylin.Koan.ZenGarden.Contracts]
source: src/Koan.ZenGarden.Contracts/
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: reviewed
  scope: contract assembly, Zen Garden runtime suite, layered connector builds, and semantic activation manifest
---

# Koan.ZenGarden.Contracts technical reference

## Ownership

This dependency-free assembly owns the shared wire-neutral vocabulary used by the Zen Garden runtime and connectors:

- `IZenGardenClient` and catalog/subscription/capability-wish values;
- `IZenGardenInitializationProvider`, `ZenGardenConnectionIntent`, and `ZenGardenOfferingResolution`;
- `ToolFqid` identity parsing and matching; and
- immutable tool snapshots and progress events.

It contains no Koan module, dependency-injection registration, client implementation, transport, or provider election.

## Layered activation

A connector may reference this assembly and implement optional Zen Garden configuration branches. Those branches become
live only when `Sylin.Koan.ZenGarden` contributes `IZenGardenInitializationProvider`. Merely loading the contracts or a
connector does not activate the runtime.

Explicit Zen Garden intent and automatic enrichment are deliberately different:

- explicit `zen-garden://` intent is a user choice and fails closed when it cannot be satisfied;
- automatic enrichment may remain dormant or continue through the connector's ordinary discovery policy when the
  runtime is absent; and
- the connector still probes the resolved native endpoint and owns its readiness semantics.

## Compatibility boundary

Connection-intent and FQID parsing normalize identifiers case-insensitively. Capability tokens are extensible strings;
unknown values remain intent for the runtime rather than being rejected by this contract assembly. Runtime and external
wire compatibility require focused Zen Garden tests before changing these shapes.
