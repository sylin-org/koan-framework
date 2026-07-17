# ARCH-0117: Safe connector telemetry by construction

**Status**: Accepted
**Date**: 2026-07-17
**Deciders**: Framework maintainer
**Scope**: Koan-owned structured logging for connector configuration, discovery, health selection, and startup.
**Related**: ARCH-0045 · ARCH-0068 · ARCH-0087 · ARCH-0105 · ARCH-0114 · ARCH-0116

---

## Decision

`Redaction` is Koan's single credential grammar. `KoanLog` is Koan's single safe structured-log boundary.
Before dispatching an event, `KoanLog` de-identifies every string, `Uri`, and exception-derived context value
once and sends the same safe context to diagnostics and Microsoft logging. Values whose grammar cannot be
proved safe fail closed as `(masked)`; ordinary non-secret structure is preserved.

`ServiceDiscoveryAdapterBase` owns candidate, health-attempt, success, timeout, and failure narration.
Provider adapters own only protocol-specific candidate normalization and health mechanics. They do not catch
an exception merely to repeat a provider-specific success/failure log; the shared invoker already owns that
decision and its safe evidence.

`AdapterOptionsConfigurator` remains the configuration lifecycle owner. Connector configuration, discovery,
and orchestration code reports decisions through `KoanLog`, not raw `ILogger.Log*` calls with configuration,
endpoint, service URL, host, or exception values.

This policy is automatic. There is no redaction option, provider interface, application logger wrapper, or
opt-out. A focused repository gate rejects direct logger use in bounded connector initialization,
configuration, discovery, orchestration, and health source, and runtime mutation tests prove that the central
boundary masks actual credential shapes.

## Boundary

The guarantee covers values emitted by Koan-owned connector configuration, discovery, health-selection, and
startup code. It does not claim to sanitize arbitrary application logs, provider-driver internals, external
logging providers, or business payloads. Those remain their owners' responsibility.

Provenance, runtime facts, health state, and logs remain distinct information products. Each projects safe
values through its own canonical boundary; none is routed through another merely to reuse presentation.

## Why

Local redaction at dozens of call sites is distributed complexity with many omission points. A global logger
provider would overreach into application and third-party semantics. The existing `KoanLog` and shared adapter
templates are the narrowest boundaries through which Koan can make an honest guarantee.

This preserves developer delight: reference the connector and get safe, useful diagnostics automatically.
It also gives connector authors and coding agents one rule to follow and reviewers one gate to inspect.

## Consequences

- Existing safe call-site redaction becomes unnecessary at the canonical structured sink and is removed where
  equivalence is proved.
- Configuration/discovery exception objects are not forwarded raw; safe type/message context is sufficient.
- Provider-specific health success chatter disappears when the shared decision already reports success.
- Logging cost gains one bounded de-identification pass per string-like structured context value; no provider
  negotiation, reflection, or runtime registry is introduced.
- New connectors inherit the same policy without adding options or security interfaces.

## Verification

- Core sink tests mutate connection strings, signed/credentialed URIs, prose, `Uri`, and exceptions.
- Shared discovery tests prove safe logs while returning the original application-facing connection result.
- Focused connector-family tests/builds preserve configuration and provider behavior.
- The repository gate rejects direct logging in the bounded startup/configuration/discovery/health source surface.
- Active engineering docs state the exact guarantee and non-claim.
