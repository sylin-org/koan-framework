# Software Engineering (SE)

Engineering practices
- Schema validation at runtime; CI contract tests
- Invariant unit tests (Association) and mutation tests for critical rules
- Golden fixtures for hook policies; performance budget checks per step
- Idempotency tests for ProjectionTask; deterministic replay with pinned policyVersion

CI/CD
- Build, test, lint, schema validate, policy lint
- Integration tests with ephemeral Mongo/RabbitMQ
- Container image scanning and SBOM
- Feature flags for PII/Compliance; canary deploys supported

DX and tooling
- docker-compose for full stack; dotnet watch hot reload
- Seed fixtures for IntakeRecord and PolicyBundle
- Minimal dev portal: queue depths, rejects, active policy, replay controls
- CLI: post fixtures, inspect KeyIndex/ReferenceItem, tail diagnostics, trigger replays

Security and governance
- Secrets via env/KeyVault; TLS everywhere; least‑privilege Mongo/RabbitMQ users
- Audit trail: lineage, RejectionReport, policyVersion, operator actions
- Policy governance: review workflow, changelog, rollback plan

Risks and mitigations
- Rule sprawl → rule taxonomy, governance, linting, tests, time budgets
- Policy drift → DB‑backed bundle, version stamping, hot reload, CI guardrails
- Dupes/out‑of‑order → Inbox/Outbox, idempotent handlers, deterministic versions
- Mongo consistency → prefer single‑write ops; use transactions when needed; outbox transaction with writes
