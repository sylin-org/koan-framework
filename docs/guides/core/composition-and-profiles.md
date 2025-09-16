# Composition and Profiles

This note explains Koan’s composition model and the role of profiles.

## Composition model
- DI-first: AddXyz(services, options) + UseXyz(app).
- Defaults: AddDefaults()/UseDefaults() registers an opinionated baseline for fast start.
- Discovery and precedence:
  - Discovery defaults: On in non-Production, Off in Production unless `Koan:AllowMagicInProduction=true`. You can override with `Koan:Messaging:Discovery:Enabled`.
  - Explicit registrations always win over discovered modules.
- Modules are NuGet packages and self-register using `IKoanInitializer`.

## Profiles (preset bundles)
Profiles are convenience bundles that register a curated set of modules. They are optional sugar — all modules can be registered explicitly. Profiles don’t prevent customization; you can Add/Remove after applying a profile.

- Lite: Core + Web
  - Minimal hosting, options validation, health endpoints, logging/OTEL, minimal APIs.
- Standard: Lite + Data
  - Adds Data module with default Dapper/ADO.NET relational adapter (Sqlite) and minimal CRUD mappers.
- Extended: Standard + Auth + Storage
  - Adds Auth (OIDC/JWT/API Keys) and Storage (FS/S3/Azure Blob).
- Distributed: Extended + Messaging + CQRS + Webhooks
  - Adds Messaging (RabbitMQ provider), CQRS buses, outbox/idempotency, and Webhooks.

Notes
- Profiles are idempotent and safe to call once; they don’t rebuild ServiceProvider.
- Feature flags: You can enable discovery per module (e.g., `Koan:Messaging:Discovery:Enabled=true`) in Development. Explicit endpoints (e.g., `Koan:Messaging:Inbox:Endpoint`) always skip discovery.
- Production: discovery disabled by default unless `Koan:AllowMagicInProduction=true`; explicit config wins even if failing (fail-fast).
- Connection strings: Profiles don’t relocate them. Keep them under the root `ConnectionStrings` section. Provider- or source-specific overrides under `Koan:Data:{provider}:ConnectionString` and `Koan:Data:Sources:{name}:{provider}:ConnectionString` take precedence; otherwise Koan falls back to `ConnectionStrings:{name}`.
