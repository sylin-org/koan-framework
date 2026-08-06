using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Support;

public class PolymorphicIntEntityTestRoot : Entity<PolymorphicIntEntityTestRoot, int>
{
    public override int Id { get; set; }

    public sealed class Variant : PolymorphicIntEntityTestRoot<Variant>
    {
        public string? Extra { get; set; }
    }
}
