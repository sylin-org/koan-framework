# Shelved orchestration: Aspire

These projects preserve a pre-V1 experiment in automatically discovering Koan contributors from an Aspire AppHost.
They are intentionally absent from `Koan.sln`, active package discovery, generated product truth, and release scope.

## Current V1 boundary

Aspire is an application topology owner, not a Koan runtime capability. An application that uses Aspire should author
ordinary AppHost code with standard resource integrations and `WithReference`; Koan connectors consume the injected
`ConnectionStrings:*` values and Aspire service endpoints through their normal discovery adapters.

The former Koan-owned self-container lifecycle, Core evaluator SPI, and provider evaluators were removed because they
created a second lifecycle authority and had no consumer outside the shelved runtime.

## Re-entry gate

Reconsider these projects only if a concrete application need cannot be expressed cleanly with standard Aspire. Any
return must establish one lifecycle owner, avoid connector-to-AppHost dependencies, provide a graduated sample and
focused tests, and earn a new public package promise through R11 review.
