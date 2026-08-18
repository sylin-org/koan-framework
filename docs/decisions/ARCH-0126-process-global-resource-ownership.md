---
id: ARCH-0126
slug: process-global-resource-ownership
domain: Architecture
status: Accepted
date: 2026-08-18
title: A process-global resource is owned by a process-derived fact
related:
  - ARCH-0125
---

# ARCH-0126: A process-global resource is owned by a process-derived fact

## Outcome

A capability may not claim a process-global resource — standard input, standard output, a fixed port,
a lock file — implicitly, and may not claim it during composition. Ownership is derived from the
process itself: an immutable fact resolved from the environment or command line, already true before
composition begins. Composition **observes** that fact; composition never **decides** it.

Standard output is the first resource under this rule. `KoanStandardStreams.StandardOutput` reports
whether it carries diagnostics or a machine protocol, resolved once from `KOAN_MCP_STDIO` or
`--mcp-stdio`. All framework diagnostics write to `KoanStandardStreams.Diagnostics`, and console
logging sets `LogToStandardErrorThreshold` from the same fact.

## Context

`Sylin.Koan.Mcp` 1.0.0 shipped an MCP STDIO doorway that no spec-compliant client could use. Three
components each assumed they owned standard output:

- the boot report wrote to `Console`;
- `ConsoleLoggerProvider` bound the stdout handle when constructed;
- the STDIO transport framed JSON-RPC onto the same stream.

Nothing arbitrated, so a client received roughly 85 lines of log output interleaved with protocol
frames. No component was individually wrong. `EnableStdioTransport` also defaulted to `true`, so a
package reference alone was enough to seize the stream — a category error under `Reference = Intent`,
which makes a capability *available* and never entitles it to a process singleton.

The transport's own mitigation, `Console.SetOut(Console.Error)` inside its hosted service, ran after
the host had started and both other writers had already bound.

## Decision

### Ownership is process-derived, not composed

An earlier attempt at this fix introduced a claim registered during composition. It failed twice for
the same reason the original bug existed: a claim is inherently ordered after some writers have bound
and before others resolve, so correctness depended on module ordering. Placing it in the module's
reporting phase was too late; moving it to registration could not reach `IConfiguration` on every host
shape.

The rule that removes the race is that the decision cannot participate in ordering at all. It is
resolved from the process, on first touch, by whichever writer asks first — and every writer asks
before it writes.

`KoanEnv` is the nearest precedent but is initialized with `IConfiguration`, which would reintroduce
the ordering it must avoid, so this fact is resolved with no dependencies at all.

### One launch signal; configuration can only narrow

`KOAN_MCP_STDIO=1` (or `--mcp-stdio`) both selects the protocol channel and permits the STDIO
transport. `Koan:Mcp:EnableStdioTransport` may suppress it and may not enable it. Because a single
signal decides both, the transport and the diagnostic writers cannot disagree about who owns the
stream — the failure mode is designed out rather than detected.

When the switch is on without the launch signal, the transport is not hosted and says so, naming the
signal to use.

### Detection is explicit

Inference is rejected. `Console.IsInputRedirected` is the tempting auto-detect and is true under CI,
containers, pipes, and most IDE run configurations; using it would silently relocate logging for a
large class of ordinary applications — a worse and far less diagnosable failure than the one being
prevented.

## Consequences

- An ordinary application is unaffected: standard output remains its diagnostic channel.
- An MCP STDIO server emits protocol frames on standard output and every diagnostic on standard
  error, verified as zero non-JSON stdout lines against a live client.
- Hosting STDIO is a deployment decision made by whoever launches the process, which is the party
  that actually knows.
- `Console.SetOut(Console.Error)` is deleted; the ownership fact leaves it nothing to mitigate.
- The rule generalizes. A future capability wanting standard input, a fixed port, or a lock file
  follows this shape instead of inventing another claim protocol.

## Evidence

Verified against a live MCP client over STDIO: `initialize` with spec-shaped params, `tools/list`
returning correct JSON Schema, `tools/call` carrying arguments, and zero non-protocol bytes on
standard output. The same client against the published 1.0.0 fails all four.
