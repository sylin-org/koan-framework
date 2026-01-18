# MOSS-0001: Persistent Registry and Non-Destructive Adoption

## Status
Accepted

## Context
Moss maintains an in-memory service registry used by `list` and operational endpoints. After a moss restart (e.g. systemd service update), previously offered services could disappear from the registry even though their `zen-offering-*` Docker containers still existed.

This caused two failure modes:
- `list` no longer reflects the running offerings after restart.
- re-offering fails because Docker container creation errors with "already exists".

Additionally, offerings with compatibility rules (e.g. MongoDB on non-AVX CPUs) must always be validated prior to installation and during self-heal/adoption.

## Decision
1. Persist the moss registry to disk at `/etc/zen-garden/moss-registry.json`.
2. On startup, moss loads the persisted registry (best-effort).
3. On startup and during health monitoring, moss performs **non-destructive adoption**:
   - Detect `zen-offering-*` containers that are not present in the registry.
   - If the container maps to a known offering template, add it to the registry.
   - Never delete/recreate/upgrade containers as part of adoption.
4. Compatibility validation is mandatory for offer/install/adopt paths:
   - **Pass**: proceed with the template image.
   - **Fallback**: use the compatibility fallback image.
   - **Fail**: do not install; adoption records the service as degraded/incompatible without mutating the container.

5. Provide an operational reconcile endpoint:
   - `POST /api/system/reconcile`
   - Forces an immediate adoption scan (useful for debugging and for avoiding the periodic health-monitor tick).
   - Request body:
     - `drop_invalid` (bool, default `false`): if `true`, remove `zen-offering-*` containers that do not map to any known offering template.

## Consequences
- Offerings survive moss restarts and continue to appear in `list`.
- Re-offer becomes idempotent in the presence of an existing container (via adoption).
- Compatibility validation is enforced consistently and can prevent installing incompatible images.
- Adoption is intentionally conservative: containers without a matching template are left alone and are not registered.

## Notes
- Registry persistence is best-effort; failures are logged and moss continues running.
- Registry format is JSON `Vec<ServiceInfo>` to favor transparency and easy diagnostics.
