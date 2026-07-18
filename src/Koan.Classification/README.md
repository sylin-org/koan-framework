# Sylin.Koan.Classification

Field-at-rest protection as Entity metadata: mark a writable string property and keep using ordinary Koan Data APIs.

## Install

```powershell
dotnet add package Sylin.Koan.Classification
```

Keep the ordinary Koan bootstrap:

```csharp
builder.Services.AddKoan();
```

## Meaningful use

```csharp
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

In Development, the package supplies an in-memory key provider automatically. This gives a complete local-first
experience with no configuration, but its keys disappear with the process: previously stored protected values are
not readable after restart.

Outside Development, Koan refuses startup while that ephemeral provider is selected. Register an application-owned
`IClassificationKeyProvider` before `AddKoan()`:

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
