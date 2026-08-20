using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace GardenCoop.Models;

/// <summary>
/// A produce listing in the co-op. <c>[Embedding]</c> makes a normal <c>Save()</c> index its business description
/// for semantic search; referenced local providers supply the mechanics.
/// </summary>
// Width stated rather than measured: this chapter is about deterministic, offline local AI, so the vector
// space must not depend on a model being loadable when the host starts. An application whose provider is a
// hard runtime dependency anyway can omit it and let Koan measure.
[Embedding(Template = "{Name}. {Description}", Model = "all-MiniLM-L6-v2", Dimensions = 384)]
public sealed class Produce : Entity<Produce>
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}
