# Shelved Koan Aspire projection — technical note

The retained code is a historical experiment in discovering `KoanModule` implementations of
`IKoanAspireResources` and invoking them against `IDistributedApplicationBuilder`.

It is not referenced by `Koan.sln` or active packages. Re-entry requires a current product use case that standard
Aspire AppHost code cannot express more simply, plus a graduated sample, tests, package documentation, and an explicit
R11 disposition. Automatic assembly loading, module activation, temporary service providers, process-environment
mutation, and swallowed discovery failures are not accepted V1 behavior.
