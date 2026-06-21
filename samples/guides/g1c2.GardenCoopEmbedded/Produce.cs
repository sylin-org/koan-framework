using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using Koan.Data.Vector.Abstractions;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoopEmbedded;

/// <summary>
/// A produce listing in the co-op. One entity = one table row + one REST resource + one vector in the
/// in-process store. <c>[Embedding]</c> makes <c>Save()</c> embed the listing in-process (local ONNX) and store
/// the vector automatically — no explicit embed call. <c>[VectorAdapter("sqlitevec")]</c> routes those vectors
/// to the durable, file-backed sqlite-vec store. Nothing here names a server — the embedded stack is selected
/// purely by which packages this project references.
/// </summary>
[Embedding(Template = "{Name}. {Description}", Model = "all-MiniLM-L6-v2")]
[VectorAdapter("sqlitevec")]
public sealed class Produce : Entity<Produce>
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>One line wires the full REST surface: GET (list/by-id), POST, PUT, PATCH, DELETE, POST /query.</summary>
[Route("api/produce")]
public sealed class ProduceController : EntityController<Produce> { }
