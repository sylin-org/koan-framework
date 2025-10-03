# DX-0043: Connector naming for external integrations

Status: Accepted

## Contract

- **Inputs:** Inventory of Koan projects/packages that integrate with external platforms or vendor services.
- **Outputs:** Canonical naming schema, directory conventions, and documentation updates that segregate core capabilities from optional integrations.
- **Error modes:** Ambiguous feature paths, connectors published without the required suffix, or namespaces diverging from package IDs.
- **Success criteria:** Every vendor-specific project follows the `Koan.{FeaturePath}.Connector.{Vendor}` pattern, doc inventories list connectors with the new naming, and contribution guidance reflects the rule.

## Edge cases

- Connectors that span multiple vendors (multi-cloud abstractions) must either split per vendor or publish a clearly labeled façade module that references specific connectors.
- Baseline capabilities (`Koan.{FeaturePath}`, `{FeaturePath}.Core`, `{FeaturePath}.Abstractions`) remain untouched; only external integrations adopt the connector suffix.
- Tooling or sample projects that ship mocked providers should still adopt the connector naming, using a `Mock`/`Test` vendor token to avoid future renames.

## Context

Koan ships dozens of vendor touchpoints—datastores, auth providers, orchestration hosts, message brokers—but project names vary (`Koan.Data.Connector.Mongo`, `Koan.Orchestration.Connector.Docker`, `Koan.Web.Auth.Connector.Google`). This inconsistency obscures which modules are core versus optional and forces contributors to memorize local conventions. We need a single naming contract that advertises “external integration” at a glance, keeps IntelliSense groupings predictable, and reduces onboarding friction for new frameworks and samples.

## Decision

1. **Naming pattern** – All external integrations adopt `Koan.{FeaturePath}.Connector.{Vendor}` for project, assembly, namespace, and NuGet package IDs. `FeaturePath` uses the most specific functional scope already present (e.g., `Data.Vector`, `Web.Auth`, `Messaging.Inbox`).
2. **Directory layout** – Connector projects live under `src/Connectors/{FeaturePath}/{Vendor}/` (mirroring the ID) with matching solution folder entries.
3. **Documentation + inventories** – Update docs (including `docs/reference/_data/adapters.yml`, module inventories, and contributor guidance) to list connectors with the new naming, and reference this ADR as the source of truth.
4. **Automation** – Extend contributor linting or CI checks to fail when a vendor package omits the `.Connector.` segment.
5. **Effective immediately** – Existing adapter/provider projects are renamed in the current development cycle; back-compat shims are unnecessary because Koan is greenfield.

## Consequences

- **Positive:** Developers can distinguish core modules from optional connectors instantly. Project trees, samples, and package searches align, lowering cognitive overhead and improving discoverability.
- **Operational:** A single rename sprint must refactor project folders, namespaces, solution references, documentation, and packaging scripts. Automated checks ensure the pattern sticks.
- **Risk:** Temporary churn while renaming many projects; mitigate by executing in coordinated batches and validating with repo-wide builds and docs runs.

## Implementation notes

- Rename all identified vendor projects (data providers, auth providers, messaging integrations, orchestration hosts, storage backends, AI providers) following this schema.
- Update `CONTRIBUTING.md` and any engineering guidance that mentions adapter naming.
- Run full solution builds and the strict docs build to ensure renames propagate cleanly.

## References

- DX-0028 Service project naming and conventions.
- DX-0036 Sylin prefix and package IDs.
- Prior analysis on adapter naming inconsistencies (September 2025).

