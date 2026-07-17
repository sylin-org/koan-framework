# SnapVault — graduation in progress

SnapVault is Koan's large photo-management dogfood application. It exercises tenant-scoped records and blobs,
guest studio/client access, durable processing jobs, media recipes, semantic metadata, mutations, and upload
progress projected over Server-Sent Events.

The repository contains focused contracts for those slices, and the application currently builds. That is
useful framework pressure, but it is not yet a production-support or deployment claim. SnapVault is therefore
absent from `samples/README.md` until one coherent shortest path, truthful provider prerequisites, cumulative
business proof, startup/facts story, and operator boundary graduate together.

For maintainers working on that graduation:

```pwsh
dotnet build samples/applications/SnapVault/SnapVault.csproj
dotnet test tests/Suites/Samples/Koan.Samples.SnapVault.Tests/Koan.Samples.SnapVault.Tests.csproj
```

Do not present the current Docker stack, external AI/vector services, performance, or completeness as the
default Koan experience. The eventual README must lead with a local meaningful result and make every optional
service and degraded behavior explicit.
