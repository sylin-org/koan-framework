# Anti-Corruption Layer (ACL)

Protect the domain from external models by translating at the boundary.

Where to apply
- Web/API: map external DTOs to domain commands and back to responses.
- Messaging: map integration events to domain events; normalize fields and units.
- Data import/export: staging + mapping to stabilize upstream changes.

Practices
- Keep mappings in dedicated translators or modules; avoid leaking DTOs into the domain.
- Validate at the edge; keep domain rules in aggregates/VOs.
- Version contracts; be tolerant readers when consuming.

## Terms in plain language
- DTO: a simple data shape used to communicate across systems.
- Contract: the agreed structure of an API or message.
- Mapping: converting one data shape into another.
