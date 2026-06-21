using Koan.Cache.Abstractions.Policies;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Recs;

// One entity = one table row, one REST resource, one cache entry, one embedding source, and one agent tool.
// The grammar never changes as we add capabilities.
// Beat 4: [Cacheable] transparent cache. Beat 6: [Embedding] auto-vectorize → semantic search.
// Beat 7: [McpEntity] projects the SAME entity to AI agents as MCP tools, gated by the SAME [Access] rule
//         REST enforces (read open, write needs the posts:write scope). koan://entities lists it.
[Cacheable(300)]
[Embedding(Properties = new[] { "Title", "Synopsis" }, Model = "all-minilm", Async = true)]
[McpEntity(Name = "anime", Description = "Anime catalog entries")]
[Access(read: "anyone", write: "anyone")]   // open for the demo; the same gate is where a grant would govern writes
public sealed class Anime : Entity<Anime>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public int Episodes { get; set; }
    public float[]? Embedding { get; set; }   // auto-populated on Save
}

// One line wires the full REST surface: GET (list/by-id), POST, PUT, PATCH, DELETE, POST /query.
[Route("api/anime")]
public sealed class AnimeController : EntityController<Anime> { }
