using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoopEmbedded;

/// <summary>
/// A produce listing in the co-op. <c>[Embedding]</c> makes a normal <c>Save()</c> index its business description
/// for semantic search; referenced local providers supply the mechanics.
/// </summary>
[Embedding(Template = "{Name}. {Description}", Model = "all-MiniLM-L6-v2")]
public sealed class Produce : Entity<Produce>
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>The produce Entity is also a complete conventional REST resource.</summary>
[Route("api/produce")]
public sealed class ProduceController : EntityController<Produce> { }
