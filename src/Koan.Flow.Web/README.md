# Koan.Flow.Web

HTTP controllers for Flow operations and views. Auto-registered.

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
- See `../Koan.Flow.Core/TECHNICAL.md` for AggregationTags rules and options.
