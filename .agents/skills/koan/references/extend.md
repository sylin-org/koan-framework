# Capability additions

Use this reference when adding, replacing, routing, or combining a capability.

## “What could I add?”

The answer is two fetches and no local machinery — but which two depends on how specific the request already is.

**A vague outcome is not yet a choice.** “Add AI to this” could be semantic search, question answering, vision, an acting agent, or human review of model output — five different projects with different runtimes and operating costs. Read the [recipe index](https://github.com/sylin-org/koan-framework/blob/main/docs/recipes/index.md) and compose the answer yourself: *Works if* tells you which recipes this application is a small step from, *Costs* tells you what each would add to operate, and *Not yet* tells you what does not exist (there is no OpenAI, Anthropic, or Gemini connector). Offer the two or three that fit, then open the one recipe they choose. Answering with a package name skips the only part that mattered.

**A named piece goes straight through.** “Add Mongo”, “use SqliteVec” — take the row from the [capability map](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/capability-map.md), take the exact package, open its recipe for the install command, configuration keys, working code, and provider limits.

Either way, lead with an observation about *their* application before offering options, and walk the result with them.

Only when the question is genuinely open — “what *could* I add?” — is a local read worth it, and only as a guard against recommending what the application already has: `scripts/inspect-koan.ps1 -Path <root> -Format Json` reports `composedPackages`, the project's references unioned with what the lockfile shows they actually composed. A bundle pulls in pieces the project file never names. Subtract that set, then name only what the outcome earns — listing everything absent is a catalog, and the developer could already read a catalog.

Keep it honest:

- Say what a piece lets the application *do*, in business terms, before naming its package.
- A piece the shelf marks *not assessed* may still be the right answer; say plainly that nothing has been promised about it.
- A store the application already has is not replaced merely because another exists.
- Copy identifiers from the shelf; never construct one from a product name.
- If the shelf cannot be retrieved, say so and answer only from what is already referenced.

## Universal move

- Name the new business job; “add a reference” is not the outcome.
- Inspect current references, routes, data, security boundaries, facts, health, and tests.
- Verify the exact operation and provider in current docs, public types, source, and focused tests.
- Preview required now, easy later, and preserved contracts.
- Add the owning reference and configure only explicit policy.
- Prove behavior, selected composition, and corrective failure.

## Data and provider changes

Distinguish a new named source, a new empty default, and replacement of populated storage. Adding an adapter never moves data. Preserve connection and database naming treated as operational contracts. Require explicit authorization and a resumable, verifiable plan before cutover.

Keep Entity operations provider-neutral. Pin important routes rather than relying on discovery order. Check the adapter for the actual filter, paging, stream, transaction, count, isolation, or vector behavior the app needs.

## Identity and tenancy

Protect the Entity or operation, not merely the middleware pipeline. Choose a trust boundary and prove anonymous denial, allowed action, forbidden action, and deterministic tests. When tenancy participates, prove cross-tenant denial through HTTP, MCP, Jobs, events, storage, media, AI, and vectors that expose the same work.

Never invent a production identity or weaken policy to keep a demonstration working.

## AI and vectors

Turn “use AI” into a named operation with inputs, output contract, sensitivity, latency, cancellation, and failure semantics. Keep provider/model routing explicit.

Treat inference, embeddings, vector storage, and re-embedding as separate pieces. Inspect filter, paging, dimension, and durability capabilities. Do not assume Koan acquires a model artifact or creates an index. A fallback is policy only when the requested outcome explicitly allows it.

## External and security-sensitive pieces

Use current official sources for identity providers, protocols, hosted AI, remote storage, brokers, remote MCP, security rules, and platform behavior. Keep non-negotiable constraints in code and tests, not only in links.
