# Support

## Where to ask

| Need | Place |
|---|---|
| "How do I…?" with Koan | [Open a discussion](https://github.com/sylin-org/koan-framework/discussions) |
| Something looks broken | [Open an issue](https://github.com/sylin-org/koan-framework/issues) — include resolved `Sylin.*` versions and, for runtime behavior, `/.well-known/Koan/facts` |
| Security report | [Private advisory](https://github.com/sylin-org/koan-framework/security/advisories/new) — never a public issue (see [SECURITY.md](SECURITY.md)) |

## Self-serve first

Koan explains itself, and the answer is often already on the table:

- **What composed, and why?** `/.well-known/Koan/facts` (or `koan://facts`) — provider elections
  with reasons, redacted.
- **What's in my build?** `koan.lock.json` — the referenced-module composition, refreshed on every
  build.
- **Is it healthy?** `/health/live` and `/health/ready`.
- **What does this capability guarantee?** The [capability map](docs/reference/capability-map.md)
  and each capability's doc under `docs/capabilities/` state outcomes, prerequisites, and honest
  limits.
- **Worked examples**: [samples/](samples/README.md) are complete applications, and every recipe
  under `docs/recipes/` is something a person actually asked for.

## Response expectations

Koan is actively developed; the train ships frequently. Issues are triaged against the current
1.x line — updating to the latest patch is the first thing maintainers will ask, since the fix
may already be on the feed.
