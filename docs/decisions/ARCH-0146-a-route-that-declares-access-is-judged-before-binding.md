---
id: ARCH-0146
slug: a-route-that-declares-access-is-judged-before-binding
domain: Architecture
status: Accepted
date: 2026-11-04
title: A routed action that declares access is judged once, before anything is bound, and the action reads what its gate found
amends:
  - ARCH-0141
related:
  - ARCH-0143
  - ARCH-0145
  - SEC-0002
  - SEC-0004
---

# ARCH-0146: A routed action that declares access is judged once, before anything is bound, and the action reads what its gate found

## Context

ARCH-0141 put Koan's gate in front of every `[Tool]` action, before anything is read of the request. Every other routed action judged
itself in its body, after binding, so a person who was not signed in was told to fix what they sent before being told they could not
send it, and an application that resolved its address to decide (the place, the resource, the caller's standing) resolved it again in
the action, the page and the frame. The gate's answer was one of three fixed sentences, and an allow carried nothing.

**The business sentence:** say what this route needs, and let the application judge it once, before anything else.

```csharp
public sealed class Needs(Bit bit) : AccessAttribute<Bit>(bit);

public sealed class TopicsController : ControllerBase
{
    [HttpPost("/t/{tangent}/{topic}")]
    [Needs(Bit.Post)]
    public async Task<IActionResult> Reply(string tangent, string topic, ReplyInput input, CancellationToken ct)
    {
        var here = HttpContext.Features.Get<Doorway>()!;   // what the gate found
        ...
    }
}
```

The application's `IAuthorizationProvider` reads the declaration with `AccessAttribute<Bit>.Of(verb)` from the request's `MethodInfo`,
resolves the route values once, and answers `AuthorizeDecision.Refused(reason)` or `AuthorizeDecision.Allowed(found)`.

## Decision

1. **The gate runs on every routed action that declares access,** on itself or on the class it is reached through, not only on
   `[Tool]` actions: `[Access]`, `[RequireScope]` or an application's `AccessAttribute<TNeed>`. It is an authorization filter, so it runs
   before resource filters, before binding and before the action; it asks `IAuthorize` as ARCH-0141 §5 does, with the verb's
   `MethodInfo` and the route values. An operation an `EntityController<T>` answers keeps its entity's own gate.
2. **`AccessAttribute<TNeed>` is the typed base** of an application's own declaration, beside `[Access]`'s terms. Koan's floor reads none
   of it; `AccessAttribute<TNeed>.Of(verb)` returns what the verb and its class declare, every declaration holding.
3. **`[Open(why)]`** declares that nothing judges a route, and why. It opens nothing a class's declaration closes.
4. **A decision refuses with a reason, or allows with a value.** `AuthorizeDecision.Refused(reason)` answers through `Answers.Refused`,
   the reason's status, sentence and word, as a command's refusal does. `AuthorizeDecision.Allowed(value)` carries what the provider
   found, and the gate puts it on the request as a feature of its own type, which the action, a filter and a formatter read.
   `IAuthorize` stays channel-agnostic. `EntityAuthorization.Decision` returns the decision whole; `Decide` keeps answering null for an
   allow, which every listing reads.
5. **A listing asks with no resource,** as before, so a provider that judges an address defers there.
6. **A 401 carries the application's challenge.** Every `IChallenge` registered adds one challenge of `WWW-Authenticate` to each 401
   Koan's refusals answer, so a client learns where to prove who it is without reading a sentence.
7. **A start check the application turns on.** With `Koan:Web:Authorization:RoutesDeclareAccess`, the host stops at start over a routed
   action of the application's own that declares neither a gate nor `[Open]`, naming each. A controller a Koan module brings, whose
   assembly names its module identity, answers for itself.

## What this overturns

- ARCH-0141 §5's "asked by `[Tool]`'s filter": the gate is its own filter, which `[Tool]` and every declaration put on an action.
- That an undeclared route's `[Access]` on its class was read only for tools.

## Ergonomics

New public concepts: `AccessAttribute<TNeed>`, `OpenAttribute`, `AuthorizeDecision.Refuse` and `Refused(reason)`, `Allow.Value`,
`EntityAuthorization.Decision`, `IChallenge` and `AuthorizeOptions.RoutesDeclareAccess`.

## Compatibility

- A plain route whose class carries `[Access]` or `[RequireScope]` is now judged by it.
- A provider that returns `Allowed()` is unchanged; one that returns a value has it on the request.
- `Answers`' 401s carry a challenge only where an application registers one.

## Considered and rejected

| Shape | Why not |
|---|---|
| The gate as a resource filter | It would run after an application's own resource filters, so whatever they decide would be decided before who may act. |
| A binding source for what the gate found | A new attribute and binder to do what reading a feature does. |
| The allow's value under `HttpContext.Items` | A string key per application, where a feature of the value's type is found by its type. |
| `[NotATool(why)]` with the start check | No application turns the check on yet; it comes with the check's first user. |
