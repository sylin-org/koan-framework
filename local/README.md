# `local/` — working memory that must not be committed

This directory holds notes that belong to a person or a session rather than to the repository:
owner context, environment quirks, where a credential lives (never its value), and hand-off notes
between sessions.

Everything here is gitignored except this README. Nothing here is authoritative: durable decisions
belong in [docs/decisions/](../docs/decisions/), durable state in the documents indexed by
[docs/MEMORY.md](../docs/MEMORY.md).

- `NOTES.md` — free-form working memory. Create it as you need it.

**Never record a secret value here.** Record where it lives — an environment variable name, a vault
path, a password-manager entry — so the note survives a rotation and leaks nothing if it escapes.
