using Koan.Data.Core.Model;
using Koan.Data.Vector.Abstractions;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoopEmbedded;

/// <summary>
/// A produce listing in the co-op. One entity = one table row + one REST resource + one vector in the
/// in-process store. <c>[VectorAdapter("sqlitevec")]</c> routes its vectors to the durable, file-backed
/// sqlite-vec store; the embedding itself is produced in-process by the local ONNX model. Nothing here
/// names a server — the embedded stack is selected purely by which packages this project references.
/// </summary>
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
