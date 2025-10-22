---
type: GUIDE
domain: core
title: "Koan Getting Started Hub"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-01
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-01
  status: verified
  scope: docs/getting-started/overview.md
nav: true
---

# Koan Getting Started Hub

## Contract

- **Inputs**: .NET 10 SDK+, a terminal or IDE, and basic familiarity with C# web projects.
- **Outputs**: Running Koan service with REST CRUD, messaging hooks, AI integration options, and pointers for production rollout.
- **Error Modes**: Missing Koan packages, disabled container runtime (for optional steps), or adapters lacking advertised capabilities.
- **Success Criteria**: `dotnet run` serves `/api/todos`, events and Flow hooks execute, optional AI endpoints respond once a provider is configured, and you know the next docs to visit.

### Edge Cases

- **SDK drift** – mismatch between repo `global.json` and local SDK; run `dotnet --list-sdks` if builds fail.
- **Rate-limited AI** – set provider batch sizes before running semantic search at scale.
- **Vector support** – confirm the selected data adapter advertises semantic capabilities before calling `Entity.SemanticSearch`.
- **Automation scope** – run Flow hooks inside long-lived hosts (`dotnet watch run` or a hosted service) to avoid prematurely cancelled work.
- **Enterprise rollout** – coordinate security policies and environment configuration before deploying shared services.

---

## Stage 0 – Understand Koan in One Glance

Koan is a modular .NET framework built around pillars you compose as you grow:

- **Core** provides auto-registration (`builder.Services.AddKoan()`), health endpoints, boot reports, configuration helpers, and observability.
- **Data** delivers entity-first persistence across SQL, NoSQL, JSON, and vector stores with static helpers (`All`, `Query`, `AllStream`, `Page`).
- **Web** supplies MVC controllers, payload transformers, OpenAPI, and GraphQL surfaces.
- **AI** adds chat, embeddings, vector search, and RAG helpers.
- **Flow** orchestrates intake ➜ standardize ➜ projection pipelines and the semantic pipeline DSL.

Everything starts minimal and grows by intent—add packages, not boilerplate.

---

## Stage 1 – Launch the Service (≈5 minutes)

1. **Initialize the project**
   ```powershell
   mkdir my-koan-app
   cd my-koan-app
   dotnet new web
   dotnet add package Koan.Core Koan.Web Koan.Data.Connector.Sqlite
   ```
2. **Model the entity**
   ```csharp
   public class Todo : Entity<Todo>
   {
       public string Title { get; set; } = "";
       public bool IsCompleted { get; set; }
       public string Category { get; set; } = "General";
   }
   ```
3. **Expose REST automatically**
   ```csharp
   [Route("api/[controller]")]
   public class TodosController : EntityController<Todo> { }
   ```
4. **Keep `Program.cs` minimal**

   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   builder.Services.AddKoan();

   var app = builder.Build();
   app.Run();
   ```

5. **Run and verify**
   ```powershell
   dotnet run
   curl -X POST http://localhost:5000/api/todos -H "Content-Type: application/json" -d '{"title":"Experience Koan"}'
   curl http://localhost:5000/api/todos
   curl http://localhost:5000/api/health
   ```

You now have REST CRUD, health checks, telemetry, and SQLite storage without configuration files.

---

## Stage 2 – Expand the Pattern

### Messaging in Minutes

- Add intent-driven events:
  ```csharp
  public class TodoCompleted : Entity<TodoCompleted>
  {
      public string TodoId { get; set; } = "";
      public string Title { get; set; } = "";
      public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
  }
  ```
- Emit events when state transitions:
  ```csharp
  public override async Task<ActionResult<Todo>> Put(string id, Todo todo)
  {
      var result = await base.Put(id, todo);
      if (todo.IsCompleted)
      {
          await new TodoCompleted { TodoId = todo.Id, Title = todo.Title }.Send();
      }
      return result;
  }
  ```
- Reference a messaging transport when ready:
  ```powershell
  dotnet add package Koan.Messaging.InMemory
  ```

### AI When You Need It

- Install providers (Ollama local, or hosted equivalents):
  ```powershell
  dotnet add package Koan.AI.Ollama
  ```
- Inject `IAi` for chat + semantic search:

  ```csharp
  public class TodosController : EntityController<Todo>
  {
    private readonly IAi _ai;
    public TodosController(IAi ai) => _ai = ai;

      [HttpPost("{id}/suggestions")]
      public async Task<ActionResult<string>> GetSuggestions(string id)
      {
          var todo = await Todo.Get(id);
          if (todo is null) return NotFound();
      var suggestion = await _ai.ChatAsync(new AiChatRequest
      {
        Messages = [ new() { Role = AiMessageRole.User, Content = $"What should I do after completing: {todo.Title}?" } ]
      });
      return Ok(suggestion.Choices?.FirstOrDefault()?.Message?.Content);
      }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Todo>>> SemanticSearch([FromQuery] string query)
      => Ok(await Todo.SemanticSearch(query));
  }
  ```

- Skip the AI step when providers are unavailable; everything else still works.

---

## Stage 3 – Automate with Flow

- Enrich workflows with Flow hooks:
  ```csharp
  Flow.OnUpdate<Todo>(async (todo, previous) =>
  {
      if (todo.IsCompleted && !previous.IsCompleted)
      {
          await new TodoCompleted { TodoId = todo.Id, Title = todo.Title }.Send();
      }
      return UpdateResult.Continue();
  });
  ```
- Promote long-running work into semantic pipelines:
  ```csharp
  await Todo.AllStream()
      .Pipeline()
      .ForEach(todo => todo.Status = "processing")
      .Save()
      .ExecuteAsync();
  ```
- Swap providers by intent:
  ```powershell
  dotnet add package Koan.Data.Connector.Postgres Koan.Data.Vector
  ```

Flow pipelines unify intake, AI enrichment, and messaging without bespoke orchestration.

---

## Stage 4 – Harden for Production

- **Security**: Enable HTTPS, configure auth providers, and review policies in `docs/guides/authentication-setup.md`.
- **Observability**: Wire OpenTelemetry or capture boot reports; see [Flow monitoring](../reference/flow/index.md#monitoring--diagnostics).
- **Configuration**: Layer settings via `Configuration.Read` helpers and environment variables.
- **Health & Resilience**: Add custom `IHealthContributor` checks, implement retry policies, and monitor Flow stage depth.
- **Deployment**: Use `koan export compose --profile Local` or Aspire integration for service meshes.

---

## Stage 5 – Scale Adoption

- Read [Enterprise Adoption Guide](./enterprise-adoption.md) for policy, governance, and rollout strategies.
- Compare Koan with adjacent stacks in the [architecture comparison](../architecture/comparison.md).
- Explore samples under `samples/` for end-to-end implementations (API, messaging, AI, Flow, automation).
- Review ADRs like [DX-0041](../decisions/DX-0041-docs-pillar-consolidation.md) to understand documentation governance.

---

## Reference Map

- Pillars: [Core](../reference/core/index.md), [Data](../reference/data/index.md), [Web](../reference/web/index.md), [AI](../reference/ai/index.md), [Flow](../reference/flow/index.md), [Messaging](../reference/messaging/index.md).
- Recipes & automation: [Flow Pillar Reference](../reference/flow/index.md#semantic-pipelines), [Semantic Pipelines Playbook](../guides/semantic-pipelines.md).
- Troubleshooting: [Support Troubleshooting Hub](../support/troubleshooting.md).
- Tooling: [ASPIRE Integration](../ASPIRE-INTEGRATION.md), Koan CLI (`scripts/` & `packaging/Koan-cli`).

Keep iterating—Koan grows with your intent, not against it.

