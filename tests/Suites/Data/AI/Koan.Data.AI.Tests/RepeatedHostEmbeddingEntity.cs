using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

namespace Koan.Data.AI.Tests;

[Embedding]
public sealed class RepeatedHostEmbeddingEntity : Entity<RepeatedHostEmbeddingEntity, string>
{
    public string Text { get; set; } = "";
}
