Reference `Sylin.Koan.Jobs`, define the work as an Entity implementing `IKoanJob<T>`, and explicitly require durable persistence:

```csharp
[JobPersistence(JobPersistenceMode.DataStore)]
public sealed class SendDigest : Entity<SendDigest>, IKoanJob<SendDigest>
{
    public static Task Execute(
        SendDigest job,
        JobContext context,
        CancellationToken ct) => /* idempotent work */;
}

await new SendDigest().Job.Submit();
```

Keep the normal `AddKoan()` call; Jobs needs no manual registration. To satisfy `DataStore`, also reference and configure a durable Koan Data provider such as SQLite, PostgreSQL, SQL Server, or MongoDB.

The failure behavior is intentional:

- Without `[JobPersistence(DataStore)]`, the host starts using the automatic in-memory ledger. Jobs can retry and remain inspectable only for that process lifetime; queued work and history are lost on restart.
- With `[JobPersistence(DataStore)]`, composition fails at startup when no durable Data provider is available. Koan reports that the durability requirement cannot be honored and directs the operator to add a qualifying provider or relax the requirement; it never silently downgrades declared durable work to memory.

Execution remains at-least-once even with durable storage, so externally visible effects must be idempotent.
