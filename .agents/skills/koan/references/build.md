# First useful slice

Use this reference when a new application or domain slice needs a concrete starting shape.

## Start with one sentence

Capture:

- **Intent:** what one user or operator can do.
- **Expression:** the Entity, operation, route, job, prompt, or resource that makes it visible.
- **Guarantee:** what survives, who may act, and what failure says.
- **Owner:** the application concept that owns the rule.

If the outcome contains several independent “and” clauses, choose the earliest useful vertical slice. Keep the rest as additive pieces.

## Shape the slice

1. Choose one Entity or application operation as the vocabulary.
2. Choose only the Data, Web, identity, Jobs, AI, vector, storage, media, Communication, MCP, or Canon pieces required by that journey.
3. Reference those capabilities and keep one `AddKoan()` composition.
4. Put real policy—authorization, routing, tenant isolation, retries, prompts, recipes—at its owning boundary.
5. Exercise the public journey, selected composition, and one useful corrective failure.

For an existing application, preserve routes, payloads, identity behavior, database names, data, and topology unless the request changes them. Adopt one aggregate or capability first; do not rename the application's language merely to resemble an example.

## A good first slice

A developer can narrate the stack in business language, every referenced piece participates, the chosen provider is visible, one public journey works, and a missing or denied dependency fails usefully.
