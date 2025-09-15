# Koan Framework Proposal: Zero‑Scaffolding, Cloud‑Native, Agent‑Ready

This proposal adapts and supersedes the previous "New Framework Proposal" to Koan.

Key updates for Koan
- Identity: Koan Framework (Koan); namespaces and NuGet org to follow this name.
- WSL: WSL is a core requirement for Linux containers locally; Docker Desktop + WSL2 recommended.
- Repo plan: This folder is a staging root to extract into a standalone repository.
- Adapter families: Relational, Document, Vector as first-class mid-abstractions; Redis added as Document adapter for v1.
- EF: optional, relational-only; Dapper/ADO.NET is the default thin path.
- Batch & pipelines: IBatchSet and repository operation pipeline included.
- AI: AddAiDefaults, MapAgentEndpoints, in-memory vector store by default; upgrade on config.

Boot semantics (DX)
- Preferred boot flow: services.AddKoan(); var sp = services.BuildServiceProvider(); set AppHost.Current, initialize KoanEnv, then call IAppRuntime.Discover()/Start.
- For console/dev, `services.StartKoan()` is available and will ensure IConfiguration is present and run discovery/start.

Reliability & graceful degradation
- Non-critical subsystems (e.g., cache, optional blob storage, outbound webhooks, background workers) must not block startup.
- Critical dependencies (primary data store, HTTP listener, required auth mode, secrets provider) fail fast and abort startup.
- Health: Degraded readiness on non-critical failures with clear logs and metrics; Unhealthy on critical failures.

The full text of the proposal is adapted from docs/10-new-framework-proposal.md with Koan-specific notes.
