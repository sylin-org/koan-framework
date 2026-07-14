# Sylin.Koan

Reference this bundle when an application needs Koan's core runtime and default data foundation.
The package carries tested, bounded dependency ranges for each component; it does not impose a shared
version on those components.

```xml
<PackageReference Include="Sylin.Koan" Version="0.17.*" />
```

Add a concrete data connector such as `Sylin.Koan.Data.Connector.Sqlite` when the application should
persist outside the included JSON default.
