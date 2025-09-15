Koan.Web.Swagger — Technical reference

Contract
- Generates OpenAPI documents for MVC controllers; serves Swagger UI endpoints.
- Inputs: Controller/action metadata, XML comments; Outputs: OpenAPI v3 JSON/YAML.

Architecture and behavior
- Controller-first; no support for inline minimal APIs by design (WEB-0035).
- Filters/transformers to shape schemas and operation docs consistently across modules.

Options
- Document title/version, server URLs, doc grouping, XML comment include paths.
- UI options: endpoint path, OAuth2/OpenID settings for Swagger UI.

Error modes and edge cases
- Missing XML comments generate reduced docs; invalid annotations logged as warnings.
- Large schemas; circular references handled via schema flattening where possible.

Security
- Integrates with Koan.Web.Auth for OAuth2/OpenID flows; supports PKCE.

References
- ./README.md
- /docs/api/openapi-generation.md
- /docs/engineering/index.md