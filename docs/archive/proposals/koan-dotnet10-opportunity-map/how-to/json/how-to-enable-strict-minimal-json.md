# How to Enable Strict Minimal API JSON with Koan.Web.Json.Strict

**Audience:** Koan service developers exposing minimal API endpoints who need deterministic JSON framing.

**Prerequisites:**

- Koan solution on .NET 10 with Koan auto-registrars enabled.
- Minimal API host (`WebApplication`) where request/response payloads should reject duplicate properties.
- Ability to update project references and `appsettings.*`.

**Inputs:**

- Project reference to `Koan.Web.Json.Strict`.
- Configuration under `Koan:Json:MinimalApis` controlling strict mode.
- Optional `IJsonTypeInfoResolver` implementations for closed-world model support.

**Outputs:**

- Minimal APIs serialize/deserialize with duplicate-property rejection and comment-free inputs.
- Consistent provenance describing strict JSON enablement.
- Reusable helper to provision strict `JsonSerializerOptions` for background jobs/tests.

**Success Criteria:**

- `dotnet build` succeeds with the new module referenced and `Koan:Json:MinimalApis:Strict = true`.
- Minimal API endpoints fail fast on `{"name":"a","name":"b"}` payloads, returning a 400 binding error.
- Provenance logs record `strict-json-registered` with settings surfaced under `Koan.Web.Json.Strict`.

**Failure Modes / Diagnostics:**

- Missing project reference results in `InvalidOperationException` for `KoanMinimalJsonOptions` resolution; add the module to the project.
- Strict mode disabled (`Strict = false`) leaves defaults untouched—verify configuration binding.
- Custom resolvers not invoked when `CombineRegisteredResolvers = false`; confirm option values and DI registrations.

---

## Step 1. Reference the Module

Add the strict JSON module alongside your existing Koan web dependencies:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Koan.Web\Koan.Web.csproj" />
  <ProjectReference Include="..\..\Koan.Web.Json.Strict\Koan.Web.Json.Strict.csproj" />
</ItemGroup>
```

Restore (`dotnet restore`) and build to confirm the module compiles in your host.

## Step 2. Opt In via Configuration

Bind strict mode using ADR-0040 naming:

```json
{
  "Koan": {
    "Json": {
      "MinimalApis": {
        "Strict": true,
        "AllowDuplicateProperties": false,
        "CombineRegisteredResolvers": true
      }
    }
  }
}
```

- `Strict` gates the entire module. Disabled hosts keep default STJ behavior.
- `AllowDuplicateProperties` defaults to `false`, enforcing the .NET 10 duplicate detection toggle.
- `CombineRegisteredResolvers` determines whether resolvers from DI are merged with the default resolver chain.

## Step 3. Rely on Koan Auto-Registrar

`Koan.Web.Json.Strict` self-registers through the Koan auto-registrar pipeline. If your host already calls `services.AddKoanAutoRegistrars()`, nothing else is required. For hosts that bypass the auto-registry, invoke the extension explicitly:

```csharp
builder.Services.AddKoanOptions<KoanMinimalJsonOptions>("Koan:Json:MinimalApis");
builder.Services.AddKoanMinimalJsonStrict();
```

This wires an `IConfigureOptions<JsonOptions>` that applies strict settings before the app starts.

## Step 4. Review Endpoint Usage

Minimal API handlers automatically benefit once strict mode is enabled:

```csharp
var app = builder.Build();

app.MapPost("/widgets", (Widget payload) => Results.Ok(payload))
   .WithName("Widgets.Create");

app.Run();
```

Submitting `{"name":"alpha","name":"beta"}` now fails with a model-binding error because `AllowDuplicateProperties = false`. Controllers and MVC remain on Newtonsoft.Json and are unaffected.

## Step 5. Provide Optional Type Info Resolvers

Closed-world DTOs can supply compile-time metadata by registering `IJsonTypeInfoResolver` implementations. The module combines them (subject to `CombineRegisteredResolvers`):

```csharp
public sealed class WidgetJsonResolver : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        => type == typeof(Widget)
            ? JsonTypeInfo.CreateJsonTypeInfo<Widget>(options)
            : null;
}

builder.Services.AddSingleton<IJsonTypeInfoResolver, WidgetJsonResolver>();
```

For ad-hoc scenarios (tests, background jobs), create strict options on demand:

```csharp
var strictOptions = KoanMinimalJson.CreateStrictOptions(resolver: new WidgetJsonResolver());
var payload = JsonSerializer.Serialize(new Widget("alpha"), strictOptions);
```

## Step 6. Validate Locally

1. Run `dotnet test tests/Koan.Web.Json.Strict.Tests/Koan.Web.Json.Strict.Tests.csproj` to ensure strict helpers behave as expected.
2. POST a payload with duplicate properties and confirm the API returns `400 Bad Request`.
3. Inspect boot logs for `Koan.Web.Json.Strict` provenance entries to verify configuration binding.

## Edge Cases & Hardening

- **Gradual Adoption:** Leave `Strict = false` while wiring resolvers; flip the switch when callers are ready for duplicate detection.
- **Interop:** If downstream clients rely on duplicate properties, scope strict mode to specific services by using a separate minimal API host.
- **Resolvers & Defaults:** `.NET` chains the default resolver first; register additional resolvers only when you need closed-world derivation.
- **Testing:** Build integration tests that assert 400 responses for duplicate property payloads to guard regressions when new DTOs are added.

## Related Links

- `Koan.Web.Json.Strict` (`src/Koan.Web.Json.Strict`) – strict minimal JSON module and options.
- Tests (`tests/Koan.Web.Json.Strict.Tests`) – coverage for duplicate detection and resolver wiring.
- ADR `ARCH-0040` – configuration naming guidance.
- `Koan.Web.Sse` (`src/Koan.Web.Sse`) – companion module for streaming endpoints using shared async helpers.
