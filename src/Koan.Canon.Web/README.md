# Koan.Canon.Web

HTTP controllers for Canon intake, replay, projections, and policy introspection. Auto-registered when the package is referenced.

Routes
- POST /intake/records
- POST /admin/replay
- POST /admin/reproject
- GET /views/canonical?page=1&size=20
- GET /views/lineage?q=ReferenceUlid=="01HF.."&page=2&size=10

Notes
- Controllers only (no inline endpoints).
- Views use a pagination envelope: { page, size, hasNext, total?, items }.
- Samples use first-class entity statics (Save/Get/Page/Query).
- See `../Koan.Canon.Core/TECHNICAL.md` for AggregationTags rules and options.
- Technical reference: [`TECHNICAL.md`](./TECHNICAL.md) (validated 2025-09-29).
