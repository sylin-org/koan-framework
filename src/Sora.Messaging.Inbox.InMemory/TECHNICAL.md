Sora.Messaging.Inbox.InMemory — Technical reference

Contract
- In-process inbox for development/tests; not for production.
- Inputs: Message envelopes dispatched in-memory; Outputs: handler results and logs.

Behavior
- At-least-once semantics within process; no durability; idempotency is caller’s responsibility.
- Concurrency and ordering follow the TaskScheduler/ThreadPool; no cross-process guarantees.

Options
- Max degree of parallelism; delay/retry policy for transient handler exceptions.

Edge cases
- App restarts cause message loss; long-running handlers can block shutdown if not canceled.

References
- ./README.md
- ../Sora.Messaging.Core/TECHNICAL.md