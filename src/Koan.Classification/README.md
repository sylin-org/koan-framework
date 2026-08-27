# Sylin.Koan.Classification

Field-at-rest protection as Entity metadata: mark a writable string property and keep using ordinary Koan Data APIs.

## Install

```powershell
dotnet add package Sylin.Koan.Classification
```

Keep the ordinary Koan bootstrap — Data verbs work once the host is up:

```csharp
using Koan.Core;   // AddKoan()

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

## Meaningful use

Inside the running host — a request handler, a background service, or a console app started through
`services.StartKoan()` — ordinary Entity verbs read and write; encryption rides the save:

```csharp
using Koan.Data.Core.Model;               // Entity<T>
using Koan.Data.Abstractions.Annotations; // [Pii]
using Koan.Data.Core;                     // Save, Get

public sealed class Customer : Entity<Customer>
{
    [Pii] public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

var customer = await new Customer
{
    Email = "ada@example.com",
    DisplayName = "Ada"
}.Save();

var loaded = await Customer.Get(customer.Id);
```

`Email` is stored in an authenticated AES-256-GCM envelope and materializes as plaintext through supported Entity
reads. `DisplayName` is unchanged. `Save()` encrypts a persistence clone, so the caller's instance remains readable
business data.

`[Pii]`, `[Phi]`, `[Secret]`, and `[Classified("category")]` currently carry the same storage behavior. Categories
describe meaning; they do not imply masking, search, tokenization, or different cryptography.

## Local and production custody

The package supplies a local key provider automatically, so a bare reference works with no configuration. Keys
persist in a keyring under the application's own `.koan/keys/classification.json`, which means protected values
written today are still readable after a restart — the ordinary run-stop-run loop does not destroy them. Startup
reports the exact keyring path.

Local custody is not production custody. The key sits beside the data it protects, is never rotated on a schedule,
and inherits only the filesystem's protection. Koan warns about it outside Development and refuses it in Production
unless `Koan:AllowMagicInProduction` is set. A real deployment registers its own `IClassificationKeyProvider` over
whatever key service it already trusts, before `AddKoan()`:

```csharp
builder.Services.AddSingleton<IClassificationKeyProvider, ApplicationKeyProvider>();
builder.Services.AddKoan();
```

The provider owns durable custody and rotation retention. Koan owns scope derivation, envelope handling, encryption,
and decryption. A missing key, damaged envelope, authentication failure, or unsupported classified property type
fails loudly.

## Automatic composition

- Every supported Entity write path passes through one host-owned transform plan before provider I/O.
- Every supported Entity materialization path reverses that plan before returning the Entity.
- Classified Entity types are excluded from distributed Entity caching so decrypted objects do not become L2 values.
- Active hard segmentation dimensions define the opaque key scope. Referencing Tenancy therefore partitions keys by
  tenant without a Classification-specific tenant accessor or configuration.
- Startup reporting identifies AES-256-GCM, the selected key-provider type, compiled segmentation scope, and current
  exclusions.

## Boundaries

- Writable `string` properties only.
- The guarantee applies to supported Koan Data/Entity paths. Calling a raw adapter or repository directly bypasses
  the Data facade and its transforms.
- Existing plaintext values are tolerated on read for migration safety, but they are not backfilled automatically.
- Ciphertext search, blind indexes, tokenization, caller-facing masking, message/log/vector redaction, backfill, and
  cryptographic erasure are not current capabilities.
- The package is field-at-rest protection, not a complete privacy, compliance, or key-management system.

See [TECHNICAL.md](TECHNICAL.md). Key-provider authors should reference
`Sylin.Koan.Classification.Contracts`, not the functional package.
