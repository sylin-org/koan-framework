---
id: ARCH-0045
slug: ARCH-0045-foundational-soc-namespaces-and-abstractions
domain: ARCH
status: Accepted
date: 2025-08-25
---

# ADR: Foundational SoC namespaces, project root hygiene, and Abstractions separation

## Context
The greenfield baseline for Koan revealed inconsistent folder/namespace organization in foundational libraries, mixed interfaces and implementations, scattered magic values, and ambiguous type names (e.g., multiple “Runtime” types). This hurts discoverability, separation of concerns (SoC), and developer experience.

## Decision
1) Folder and namespace normalization (mandatory across Core libraries)
   - No .cs files at project roots; keep only the .csproj and companion docs (README/TECHNICAL).
   - Namespaces mirror folders: Koan.<Area>.<Domain>[.<SubDomain>]; file-scoped namespaces preferred.
   - One public class per file; nest true satellites. Keep interfaces separate unless tightly bound to the same concern.
   - DI extensions live under Extensions/ and end with the suffix “Extensions”.
   - Tunables live in Options/*Options.cs; stable literals in Infrastructure/Constants.cs. No magic values in code.
   - Conventional suffixes: Options, Policy, Selector, Factory, Diagnostics, Conventions, Initializer.

2) Project-scoped layouts (non-exhaustive maps)
   - Koan.Core
     - Hosting/{App, Runtime, Bootstrap}
     - Observability/{Health, Probes}
     - Extensions/
     - Primitives/
     - Infrastructure/{Constants, Security}
   - Koan.Messaging.Core
     - Options, Extensions, Diagnostics, Initialization, Discovery, Dispatch, Buses, Conventions, Retry, Infrastructure
   - Koan.Data.Core
     - Model, Services, Diagnostics, Options, Extensions, Indexing, Projections, Operators, Configuration, Initialization, Direct, Naming, Infrastructure, Testing
   - Koan.Media.Core
     - Keep existing structure (Extensions, Initialization, Model, Operators, Options); ensure namespaces mirror folders; add Infrastructure/Constants if needed.

3) Abstractions boundary (cross-cutting)
   - Interfaces and contracts intended for consumption without implementation dependencies live in Koan.*.Abstractions.
   - Core packages hold default implementations and policies.
   - For Messaging and Data, move interfaces into their Abstractions packages if duplicates exist; reference Abstractions from Core.
   - Greenfield posture: no shims required; update all call sites within the repo.

4) Repository facade posture (Data)
   - Keep RepositoryFacade public for advanced scenarios, but document it as second-class.
   - Samples/docs must prioritize first-class model statics: All/Query/FirstPage/Page/Stream.
   - Avoid expanding the public surface around the facade without an ADR.

5) Scope of project split
   - Keep projects together when they won’t be used separately in realistic scenarios; avoid premature package splits.

## Scope
Applies to foundational libraries: Koan.Core, Koan.Data.Core, Koan.Messaging.Core, Koan.Media.Core. Does not change external HTTP APIs directly; controllers remain the only HTTP route entry points per WEB-0035.

## Consequences
Breaking but clarifying moves and renames are allowed in this baseline:
- Namespaces change to reflect folder moves; no .cs files remain at project roots.
- Constants centralized; redundant AssemblyInfo files removed where SDK-style projects are used.
- Representative renames for clarity (examples):
  - Core: KoanApp → AppHost; KoanEnv → AppEnvironment; IKoanRuntime → IAppRuntime; KoanInitialization → AppBootstrapper; KoanBootstrapReport → BootReport; StartupProbeService → BootProbeService.
  - Messaging: Initializer → MessagingInitializer; Naming → NamingConventions; Negotiation → NegotiationConventions.
  - Data: DataService → DataRuntime; RepositoryFacade → GenericRepositoryFacade (remains public; documented as second-class).
  - Remove duplicate/ambiguous “KoanRuntime” naming in Data.Core.

## Implementation notes
- Move files into the target folder structure and update namespaces accordingly.
- Add Infrastructure/Constants.cs to each project; replace magic strings with constants; move tunables into Options/*Options.cs.
- Prefer file-scoped namespaces; ensure nullable and implicit usings are consistently configured.
- Update per-project README.md and TECHNICAL.md per ARCH-0042 to reflect the final structure and extension points.
- Update all references across the solution; build and run tests; run strict docs build.

## Follow-ups
- Audit interfaces across pillars to ensure they live under Abstractions packages where appropriate.
- Align all samples and documentation with first-class model statics in Data (DATA-0061).
- Re-evaluate future package splits for Koan.Core when/if separate consumption becomes realistic.

## References
- ARCH-0040 — Config and constants naming
- ARCH-0042 — Per-project companion docs
- WEB-0035 — EntityController transformers
- DATA-0061 — Data access pagination and streaming
