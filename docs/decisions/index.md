# Architecture Decision Records

ADRs grouped by topic. The canonical files live in this folder; this index links them by domain prefix.

- Architecture (ARCH)
  - ARCH-0001: See foundational boot and composition ADRs (e.g., 0040 config/constants naming)
- Data (DATA)
  - DATA-0047: Postgres adapter — 0047-postgres-adapter.md
  - DATA-0048: Docker probing for tests — 0048-standardize-docker-probing-for-tests.md
  - DATA-0049: Direct commands API — 0049-direct-commands-api.md
  - DATA-0050: Instruction constants — 0050-instruction-name-constants-and-scoping.md
  - DATA-0051: Direct routing via instruction executors — 0051-direct-routing-via-instruction-executors.md
  - DATA-0052: Dapper boundary; Direct uses ADO.NET — 0052-relational-dapper-boundary-and-direct-ado.md
- Web (WEB)
  - WEB-0035: EntityController transformers — 0035-entitycontroller-transformers.md
  - WEB-0041: GraphQL module — 0041-graphql-module-and-controller.md
  - WEB-0042: GraphQL naming and discovery — 0042-graphql-naming-and-discovery.md
- Messaging (MESS)
  - MESS-0021: Messaging capabilities and negotiation — 0021-messaging-capabilities-and-negotiation.md
  - MESS-0022: Provisioning defaults and dispatcher — 0022-mq-provisioning-aliases-and-dispatcher.md
  - MESS-0023: Alias defaults and DefaultGroup — 0023-alias-defaults-default-group-and-onmessage.md
- DevEx (DX)
  - DX-0037: Tiny templates family — 0037-tiny-templates-family.md
  - DX-0038: Auto-registration — 0038-auto-registration.md
  - DX-0040: Config and constants naming — 0040-config-and-constants-naming.md

Note: Historical numeric filenames are kept; prefixes here are for navigation only. New ADRs should adopt PREFIX-#### in filenames.
