# Sylin.Koan.Classification.Contracts

Inert extension contracts for application- or adapter-owned Classification key custody.

## Install

```powershell
dotnet add package Sylin.Koan.Classification.Contracts
```

## Meaningful use

Implement a key provider without activating field-at-rest behavior in the provider assembly itself:

```csharp
public sealed class ApplicationKeyProvider : IClassificationKeyProvider
{
    public ClassificationDataKey GetActiveKey(string scope)
        => throw new NotImplementedException("Resolve the active durable key for this opaque scope.");

    public ClassificationDataKey GetForDecrypt(string keyId)
        => throw new NotImplementedException("Resolve this retained key id from durable custody.");
}
```

The application that wants Classification behavior references `Sylin.Koan.Classification`, registers the provider
before `AddKoan()`, and keeps all scope/encryption mechanics inside Koan:

```csharp
builder.Services.AddSingleton<IClassificationKeyProvider, ApplicationKeyProvider>();
builder.Services.AddKoan();
```

This package contains no Koan module and performs no registration or startup work. Referencing it alone is a
deterministic no-op.

## Boundaries

- This package defines custody interoperability; it does not supply encryption behavior or a key store.
- The provider owns durable material protection, rotation retention, concurrency, and disposal.
- Koan supplies opaque scopes and key ids. Provider code should not infer tenant or other business meaning from them.
- Erasure workflows, KMS-specific configuration, vault health, and compliance policy are outside this contract.

See [TECHNICAL.md](TECHNICAL.md).
