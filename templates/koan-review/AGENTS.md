# Working on this Koan application

This is a Koan application. Koan is a .NET meta-framework in which **a package reference is the
intent**: referencing a capability makes it available, and `AddKoan()` composes everything referenced,
once. Application code states business meaning; the framework owns composition, provider election,
lifecycle, and explanation.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

## What this application composes

This app's review capability registers its infrastructure through `AddKoan()`; the app declares its
business queue explicitly with `AddKoanReview`, and SQLite persists the entities. The startup journey
proves queue registration, approve/reject persistence, and duplicate-name refusal, then prints
PROBE PASS.

Change what the model drafts and who reviews it - keep generated output Pending until a person signs
off, and filter public reads on the reviewed state.

## Two rules that shorten most tasks

1. **To add a capability, add its package reference.** Do not write provider registration or manual
   composition beside `AddKoan()`.
2. **Never construct an identifier from a product name.** Package identifiers are exact; copy them
   from the csproj references already here.

For anything beyond this app, start at the
[recipe index](https://github.com/sylin-org/koan-framework/blob/main/docs/recipes/index.md) for an
outcome, the [capability map](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/capability-map.md)
for a named piece, or [llms.txt](https://github.com/sylin-org/koan-framework/blob/v1.0.0/llms.txt) to
index the whole documentation set.
