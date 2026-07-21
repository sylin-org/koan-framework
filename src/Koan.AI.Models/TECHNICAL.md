# Sylin.Koan.AI.Models technical contract

## Activation and ownership

Generated module activation registers one singleton `IModelService`. Static `Model.*` methods resolve it from active
`AppHost`. Model catalog entries use Koan Data; adapter discovery/routing uses the AI adapter registry and inert shared
model vocabulary.

## Operation routing

Search, pull, transformation, deployment, listing, and management are resolved against adapter capabilities and model
managers. Pull registers the resulting local model. Transform operations return logical `JobRef` identities.
Deployment records runtime state only after the selected adapter succeeds. History/audit read persisted catalog facts.

## Failure and limits

No capable adapter, ambiguous destination, missing model/file, unsupported format, external API failure, and
cancellation stay visible. The package does not supply conversion binaries, inference runtimes, storage capacity,
license/security review, cross-adapter transactions, or automatic production rollout policy.
