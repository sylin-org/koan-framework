# Sylin.Koan

Reference this bundle when an application needs Koan's core runtime, Entity data grammar, and the
zero-infrastructure JSON fallback. The package carries tested, bounded dependency ranges for each
component; it does not impose a shared version on those components.

Koan is source-first until the coherent package wave is published and observed. This README describes
the bundle shape; it is not a current public-feed installation instruction.

Add `Sylin.Koan.Data.Connector.Sqlite` for Koan's durable Level-1 local application path. JSON remains
appropriate for bounded local files, seeds, and smoke scenarios; its presence is not a production
durability claim.
