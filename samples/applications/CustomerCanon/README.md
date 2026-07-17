# CustomerCanon — graduation in progress

CustomerCanon exercises Koan's Canon runtime with one customer pipeline:

```text
raw customer → validation and normalization → enrichment → canonical customer
```

`Customer : CanonEntity<Customer>` owns the canonical state. Two `ICanonPipelineContributor`
implementations own validation and enrichment, while `CustomerPipelineRegistrar` declares their order.
`CanonSampleModule : KoanModule` contributes that application-owned pipeline to the Canon runtime; there is
no compatibility registrar or custom module identity.

From the repository root, the current source builds and can be inspected with:

```pwsh
dotnet run --project samples/applications/CustomerCanon -- --urls http://localhost:5000
```

This application has not yet passed Koan's golden-sample graduation contract, so it is deliberately absent
from `samples/README.md`. Its next assessment must define one deterministic customer result, prove the HTTP
surface and composition facts in a focused test, remove any redundant host ceremony, and document exact
failure behavior before it becomes public curriculum.
