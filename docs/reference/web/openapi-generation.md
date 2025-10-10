---
type: REF
domain: web
title: "OpenAPI generation"
audience: [developers, architects]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/reference/web/openapi-generation.md
---

# OpenAPI generation

## Contract

- Inputs: Web app with `AddKoan()` and controllers.
- Outputs: OpenAPI document reflecting controllers, models, and transformers.
- Error modes: Missing controller discovery, schema drift, hidden endpoints not documented.
- Success criteria: Accurate OpenAPI for consumers; CI rebuilds catch drift.

### Edge Cases

- Custom formatters may need explicit schema generators.
- View shaping (transformers) should be represented via documented DTOs.
- Versioning: Keep doc title/version aligned with `version.json`.

---

## Generation & customization

- OpenAPI is generated from controllers discovered by Koan.
- Customize with filters for operation IDs, tags, and schema naming.

## CI integration

- Use the docs build pipeline to regenerate on PRs and flag drift.

## Related

- Web HTTP API, Reference Web index, ADR WEB-0035
