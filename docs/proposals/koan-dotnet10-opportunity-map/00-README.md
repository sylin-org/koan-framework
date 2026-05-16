# Koan + .NET 10 Opportunity Map (Agent Pack)

**Purpose**: Implement the highest-value .NET 10 upgrades in Koan with minimal cognitive load and maximum DX gains.

**How to use this pack (for agentic implementers):**

1. Read `01-Executive-Summary-and-Architecture.md` for the big picture.
2. Execute Group **A** guides first (high value / low effort).
3. Then Group **B** (medium), and Group **C** (deep changes).
4. Each task file contains: _intent → plan → guardrails → acceptance criteria → tests_.

**Priorities (from the sponsor):**

- Pillar focus order: **Data, AI, Vector, MCP/Agents, then everything else.**
- Providers prioritized: **Sqlite, Postgres, Mongo, Weaviate, RabbitMQ**.
- **Sane defaults**, configurable. **Breaking changes welcome** for better long-term design.
- **Keep Newtonsoft.Json** for global/open polymorphism; use STJ _only_ where safe/closed.
  See design notes in A03.
- Stay clear of EF in core; Koan Data remains polyglot and provider-agnostic. fileciteturn0file16

**Koan context links**: Architecture Principles, Module Ledger, Capability Map, and Comparator.
These are referenced throughout: fileciteturn0file16 fileciteturn0file15 fileciteturn0file13 fileciteturn0file14

---

## File Map

- `01-Executive-Summary-and-Architecture.md`
- `10-Group-A-Overview.md` and `A/*` tasks
- `20-Group-B-Overview.md` and `B/*` tasks
- `30-Group-C-Overview.md` and `C/*` tasks
- `90-Appendix-References.md` (links/citations to .NET 10 + Koan docs)
- `how-to/diagnostics/how-to-surface-koan-app-identity-pane.md` (surface the `[KOAN]` startup block)
