# OrderIntake — graduation in progress

OrderIntake is reserved for the next complete-application story: accept a bounded order batch, process it
as a durable job, and produce a verifiable receipt through a local zero-infrastructure path.

The current source still contains the legacy adapter-benchmark implementation inherited during the semantic
portfolio move. It builds, but it is intentionally absent from `samples/README.md` and is **not** a current
Koan usage, provider-performance, or deployment recommendation. Its SignalR dashboard, direct SQLite tuning,
adapter rankings, and container-first flow are scheduled for break-and-rebuild rather than documentation.

The graduation work must replace those mechanics with business-aligned order intake, typed optional provider
channels, corrective unavailable-provider errors, startup/facts visibility, and one cumulative executable
contract. Until that contract passes, use [DevPortal](../DevPortal/README.md) for named provider composition
and [GardenCoop](../../journeys/GardenCoop/README.md) for cumulative capability growth.
