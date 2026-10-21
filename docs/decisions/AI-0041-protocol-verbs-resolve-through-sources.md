---
id: AI-0041
slug: protocol-verbs-resolve-through-sources
domain: AI
status: Accepted
date: 2026-10-21
title: Protocol verbs resolve through AI sources; an explicit source is never weakened
related:
  - AI-0015
  - AI-0039
---

# AI-0041: Protocol verbs resolve through AI sources; an explicit source is never weakened

## Context

`Client.Imagine`, `Transcribe`, `Speak`, `Rerank`, `Edit` and `Render` (and their `*Result` and `StreamSpeak`
forms) find their adapter without the category router. `Client.FindAdapter` merged the explicit or scoped source,
looked it up with `IAiAdapterRegistry.Get(source)`, which keys on an adapter id and not a source name, and on a miss
(or when that adapter lacked the verb's interface) took the first registered adapter implementing the interface,
ordered by id. An AI source is a named collection whose adapter is `Source.Provider` (AI-0015), so
`Source = "fast"` with `Koan:Ai:Sources:fast:Provider = b-provider` was served by `a-provider`, and a misspelled or
disabled source succeeded against whichever provider sorted first, possibly another vendor, cost or data residency.
`Chat` and `Embed` fail loud on the same input through `AiCategoryRouter.ResolveSource`, so behavior depended on the
verb (ledger NK-033).

## Decision

**A protocol verb resolves its adapter by the rules of the category router: explicit source, then scoped source, then
the elected default. An explicit or scoped source is required and never weakened to another provider; ambiguity is
an error, never a first match.**

1. A source in play is the explicit `Source` option, else the `AiCategoryScope` entry for the verb. A `name::member`
   reference resolves through `name`.
2. The source must exist, be enabled, and its `Provider` must resolve to a registered adapter implementing the
   verb's interface. Otherwise the verb throws `InvalidOperationException` naming the source and the usable
   sources (enabled sources whose provider implements the interface), as the router does.
3. With no source in play, the providers implementing the interface are the candidates, except a provider whose every
   source is disabled. One candidate serves. Two or more are elected by the strictly highest priority among their
   enabled sources (a provider with no source ranks last). A tie, including every default, throws and lists the tied
   providers and sources, and says to pass `Source`, use `Client.Scope`, or raise one source's `Priority`.
4. No candidate throws the existing "No adapter registered for ..." error.

The mechanics are one internal type, `ProtocolAdapterResolver` in `Koan.AI.Resolution`, called by
`Client.FindAdapter`; every verb overload reaches it through that method. `AdapterResolver` (capability and adapter
id, used by Models and Eval) is unchanged. No public signature changes.

## Compatibility

Behavioral, in the direction of failing loud:

- A `Source` or scope naming a source that is unknown, disabled, or whose provider lacks the verb now throws; it
  used to run on the first adapter implementing the interface.
- A `Source` that is an adapter id with no source registered for it no longer resolves. Provider packages publish a
  source named by their provider id, so `Source = "ollama"` keeps working wherever the provider is configured; a
  provider injected into the adapter registry without a source must also register one.
- Two providers implementing a verb, with equal source priority and no `Source`, used to serve silently from the
  one sorting first by id; they now throw until `Source`, a scope or a distinct `Priority` elects one. Applications
  with exactly one provider per verb are unaffected.
- `Client.Scope(...)` and `Source` now apply to the verb with the router's strictness.

## Evidence

`ProtocolVerbSourceResolution` (Koan.Tests.AI.Unit), over fake adapters implementing all six interfaces and a real
`AiSourceRegistry`, for each of the six verbs: an explicit source and a scoped source select their provider (and an
explicit source overrides a scope); a member reference resolves; an unknown source, a scoped unknown source and a
disabled source throw naming usable sources; a source whose provider lacks the verb, or is not registered, throws;
one provider serves unqualified; two equal providers throw; the higher-priority source elects its provider; a
provider with only disabled sources is not elected. 50 of its 58 cells failed before this change.
