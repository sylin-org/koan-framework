# Redis adapter

This guide covers Koan's Redis data adapter. It provides a lightweight key/value store with LINQ filtering in-memory, simple paging guardrails, and instruction support. Use it for caches, simple aggregates, and ephemeral data.

## Capabilities

- Query: LINQ (in-memory filtering) and basic Count
- Paging: default and max limits via options; client-side skip/take
- Writes: upsert, delete, and non-atomic batch (best-effort)
- Instructions: data.ensureCreated, data.clear
- Health: PING check via IConnectionMultiplexer

## Package & registration

Project: `src/Koan.Data.Redis`

```csharp
services.AddKoanCore();
services.AddKoanDataCore();
services.AddRedisAdapter();
```

## Configuration

First-win configuration keys:

- Koan:Data:Redis:ConnectionString
- Koan:Data:Sources:Default:redis:ConnectionString
- ConnectionStrings:Redis
- ConnectionStrings:Default

Optional:

- Koan:Data:Redis:DefaultPageSize (default 50)
- Koan:Data:Redis:MaxPageSize (default 200)

Discovery defaults:

- Local dev: 127.0.0.1:6379
- In-container: redis:6379

## Naming

Keys are prefixed with the storage name resolved by StorageNameRegistry so different sets don’t collide. You can override naming via `INamingDefaultsProvider` or attributes.

## Usage

```csharp
var repo = provider.GetRequiredService<IDataService>()
                   .GetRepository<Person, string>("redis");

await repo.UpsertAsync(new Person { Id = "1", Name = "Ada", Age = 37 });
var adults = await ((ILinqQueryRepository<Person, string>)repo)
    .QueryAsync(p => p.Age >= 18);
var total = await ((ILinqQueryRepository<Person, string>)repo)
    .CountAsync(p => p.Age >= 18);
```

Batching is best-effort and not atomic:

```csharp
var batch = repo.CreateBatch();
await batch.UpsertAsync(new Person { Id = "2", Name = "Bob", Age = 29 });
await batch.DeleteAsync("1");
await batch.SaveAsync(new BatchOptions { RequireAtomic = false });
```

Instructions:

```csharp
var exec = (IInstructionExecutor<Person>)repo;
await exec.ExecuteAsync<bool>(Instruction.Create("data.ensureCreated"));
await exec.ExecuteAsync<int>(Instruction.Create("data.clear"));
```

## Notes

- LINQ filters and paging are performed client-side after fetching the keyspace for the set.
- Use Redis for speed and simplicity; prefer relational adapters for complex querying.
