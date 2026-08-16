using Koan.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Mcp;
using Koan.Tenancy;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Evals.Grammar;

// The Entity as application vocabulary.
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

// One Entity acquiring semantic indexing and an agent surface as additive capabilities.
[Embedding(Template = "{Title}. {Summary}")]
[McpEntity(Name = "knowledge", Description = "Curated knowledge")]
public sealed class KnowledgeItem : Entity<KnowledgeItem>
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
}

// The HTTP projection: additive, over the same model.
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;

// Durable work as an Entity that owns its own execution.
public sealed class Review : Entity<Review>, IKoanJob<Review>
{
    public string Subject { get; set; } = "";

    public static Task Execute(Review self, JobContext ctx, CancellationToken ct)
        => Task.CompletedTask;
}

public static class Grammar
{
    // Instance, collection, and static Entity operations.
    public static async Task EntityOperations(CancellationToken ct)
    {
        var todo = await new Todo { Title = "Ship one useful thing" }.Save(ct);
        var same = await Todo.Get(todo.Id, ct);
        var open = await Todo.Query(x => !x.Done, ct);
        var page = await Todo.FirstPage(25, ct);
        await new[] { new Todo { Title = "Compose" }, new Todo { Title = "Prove" } }.Save(ct);
        await foreach (var item in Todo.AllStream(ct)) { _ = item; }
        _ = (same, open, page);
        await todo.Remove(ct);
    }

    // Scoped, nestable context switches that restore automatically.
    public static async Task ScopedContext(CancellationToken ct)
    {
        using (Tenant.Use("acme"))
        using (EntityContext.Source("Archive"))
        using (EntityContext.Partition("north"))
        {
            _ = await Todo.FirstPage(25, ct);
        }

        using (EntityContext.Adapter("sqlite"))
        {
            _ = await Todo.Query(x => x.Done, ct);
        }
    }

    // Model-powered operations through the ambient client.
    public static async Task AiOperations(CancellationToken ct)
    {
        var answer = await Client.Chat("Summarize today's knowledge.", ct);
        var meaning = await Client.Embed("provider-neutral composition", ct);
        _ = (answer, meaning);
    }
}
