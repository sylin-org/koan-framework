# Sylin.Koan

Reference this bundle when an application needs Koan's core runtime, Entity data grammar, and the
zero-infrastructure JSON fallback. The package carries tested, bounded dependency ranges for each
component; it does not impose a shared version on those components.

```xml
<PackageReference Include="Sylin.Koan" Version="0.17.*" />
```

Add `Sylin.Koan.Data.Connector.Sqlite` for Koan's durable Level-1 local application path. JSON remains
appropriate for bounded local files, seeds, and smoke scenarios; its presence is not a production
durability claim.
