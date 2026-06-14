# How to Publish Koan OpenAPI 3.1 Docs with Swagger UI

**Audience:** Koan app developers enabling interactive API docs.

**Prerequisites:**
- Koan solution on .NET 10 with `Koan.Web` and `Koan.Web.OpenApi` restored.
- Host project built on `WebApplication` (ASP.NET Core minimal host) with endpoint routing.
- Access to `appsettings.*` for configuring `Koan:OpenApi` and `Koan:Web:Swagger` sections.

**Inputs:**
- Project file referencing the Koan OpenAPI and Swagger connector modules.
- Host configuration (environment, Koan magic flags, optional route overrides).
- Desired exposure environment (Development vs Production).

**Outputs:**
- `/openapi/{documentName}.json` endpoint serving the shared OpenAPI 3.1 document.
- Swagger UI mounted at `/swagger` (configurable) targeting the Koan OpenAPI output.
- Provenance records reporting spec version and effective route.

**Success Criteria:**
- `dotnet build` succeeds with `Koan.Web.OpenApi` and `Koan.Web.Connector.Swagger` referenced.
- Navigating to the configured Swagger UI route returns an interactive page backed by `/openapi/v1.json`.
- Production gating honors `Koan:OpenApi:Enabled` and `Koan:Web:Swagger:Enabled` (or Koan magic flag) before exposing endpoints.

**Failure Modes / Diagnostics:**
- Missing references result in `System.InvalidOperationException` when `AddOpenApi` services are absent.
- Swagger UI returns 404 if `Koan:OpenApi:RoutePattern` and UI `RoutePrefix` diverge; verify both settings.
- Production environments reject UI when neither `Koan:OpenApi:Enabled=true` nor `Koan:AllowMagicInProduction=true` is present.

`{documentName}` resolves to the logical name assigned by `AddOpenApi` (default `v1`). The placeholder must remain in the route pattern so each document can be located by its name.

---

## Step 1. Reference the Modules

Update the host project file to reference both packages:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Koan.Web\Koan.Web.csproj" />
  <ProjectReference Include="..\..\Koan.Web.OpenApi\Koan.Web.OpenApi.csproj" />
  <ProjectReference Include="..\..\Connectors\Web\Swagger\Koan.Web.Connector.Swagger.csproj" />
</ItemGroup>
```

Restore modules with `dotnet restore` and confirm the project builds before continuing.

## Step 2. Verify Service Registration

`Koan.Web.OpenApi` registers itself via `IKoanAutoRegistrar`, wiring `AddOpenApi` and default transformers. `Koan.Web.Connector.Swagger` adds Swagger UI during startup. Ensure your host opts into Koan auto-registrars (default in Koan templates). Nothing further is needed in `Program.cs` beyond `builder.Build()`.

### Optional: Explicit Enablement

If the host disables auto-registrars, call:

```csharp
builder.Services.AddKoanOpenApi(); // from Koan.Web.OpenApi
builder.Services.AddKoanSwagger(builder.Configuration); // from Koan.Web.Connector.Swagger
```

## Step 3. Configure Exposure

Key settings follow ADR-0040 (explicit reads). Add to `appsettings.json`:

```json
{
  "Koan": {
    "OpenApi": {
      "Enabled": true,
  "RoutePattern": "/openapi/{documentName}.json"
    },
    "Web": {
      "Swagger": {
        "Enabled": true,
        "RoutePrefix": "swagger",
        "RequireAuthOutsideDevelopment": true
      }
    },
    "AllowMagicInProduction": false
  }
}
```

- `Enabled` defaults to `true` outside Production. Set `false` to suppress.
- `RoutePattern` must include `{documentName}`; the default is `/openapi/{documentName}.json`.
- UI `RoutePrefix` defines the browser path (`/swagger` by default).
- `RequireAuthOutsideDevelopment` enforces `UseAuthentication/UseAuthorization` for the UI when `env.IsDevelopment() == false`.
- `AllowMagicInProduction` overrides gating for both endpoints when `true`.

## Step 4. Build & Verify

1. Run `dotnet build` to ensure services compile.
2. Launch the host (e.g., `dotnet run`).
3. Navigate to `https://localhost:5001/openapi/v1.json` and confirm the JSON payload.
4. Open `https://localhost:5001/swagger` to load the UI. The UI now reads the shared Koan OpenAPI document.

### Validation Checklist

- Pagination headers, Koan header conventions, and transformer media types appear in operation details (powered by the new transformers).
- Provenance logs include `Koan.Web.OpenApi` and Swagger UI route in boot diagnostics.
- Production environment returns 404 unless enabled explicitly.

## Step 5. Customize Document Metadata

`KoanOpenApiOptions` currently sets spec version (3.1) and document name (`v1`). For multiple documents, extend `Koan.Web.OpenApi` by registering additional document transformers or toggling `Koan:OpenApi:Enabled` per environment. Swagger UI automatically picks the default `v1` document; custom document names require matching updates:

```json
"Koan": {
  "OpenApi": { "RoutePattern": "/openapi/{documentName}.json" },
  "Web": {
    "Swagger": { "DocumentName": "admin" }
  }
}
```

Then update application code to call `ui.SwaggerEndpoint($"/openapi/admin.json", "Admin API")` (extend the connector or introduce configuration binding for multiple endpoints).

## Edge Cases & Hardening

- **Reverse Proxy / Path Base:** When hosting behind a proxy, ensure forwarded headers are configured. The UI builds URLs relative to the incoming request path, so append `ui.RoutePrefix = "docs";` or configure `SwaggerUIOptions.OAuthClientId` as required by your identity provider.
- **XML Comments:** The legacy XML comment inclusion is removed. If you need schema descriptions driven from XML docs, add a custom `IOpenApiDocumentTransformer` to `Koan.Web.OpenApi` that loads XML into the model before serialization.
- **Multiple Documents:** For multi-tenant or versioned APIs, add multiple `SwaggerEndpoint` calls. Confirm every endpoint matches an actual OpenAPI document generated by `AddOpenApi`.
- **Security Verification:** Always test UI routes in Production with and without authentication to verify `RequireAuthOutsideDevelopment` wiring.

## Related Links

- `Koan.Web.OpenApi` (`src/Koan.Web.OpenApi`) – base module providing OpenAPI 3.1 document and transformers.
- `Koan.Web.Connector.Swagger` (`src/Connectors/Web/Swagger`) – thin UI wrapper targeting Koan’s OpenAPI document.
- ADR `ARCH-0040` – configuration and constants naming guidance.
- `docs/reference/web/openapi-generation.md` – deeper reference on OpenAPI generation internals.
