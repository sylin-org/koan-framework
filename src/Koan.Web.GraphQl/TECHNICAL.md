Koan.Web.GraphQl — Technical reference

Contract
- Inputs: HTTP GraphQL queries/mutations over POST; batched queries optional.
- Outputs: GraphQL-compliant JSON response with data and errors arrays.
- Errors: Validation errors (400), execution errors reported in errors[] with 200/400 depending on policy.

Architecture
- Controller-first: GraphQL endpoint hosted via MVC controller or middleware anchored by controller route registration.
- Schema-first or code-first schema generation; resolvers call domain services/data statics.
- No inline minimal APIs; follow WEB-0035 for discoverability and testability.

Options
- Max depth/complexity, persisted queries cache, introspection toggle, playground UI toggle, batch limits, timeouts.

Security
- AuthN/Z integrated with Koan.Web.Auth; per-field authorization where supported.
- Disable introspection in production when needed; enforce depth/complexity to prevent abuse.

Data access guidelines
- Use first-class model statics for queries: MyModel.All/Query/FirstPage/Page/Stream.
- Avoid loading unbounded sets without paging; prefer streaming or explicit paging (DATA-0061).

Operations
- Structured logging for query hash, variables, and timing; redact sensitive inputs.
- Caching and persisted queries backed by distributed cache when configured.

References
- ./README.md
- /docs/guides/data/all-query-streaming-and-pager.md
- /docs/decisions/WEB-0035-entitycontroller-transformers.md