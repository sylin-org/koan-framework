using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Support;

public class PolymorphicEntityTestMedia : Entity<PolymorphicEntityTestMedia>
{
    public string Kind { get; set; } = "";
    public PolymorphicEntityTestMedia? Related { get; set; }

    public sealed class Anime : PolymorphicEntityTestMedia<Anime>
    {
        public int? Episodes { get; set; }

        [Timestamp(OnSave = true)]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class Manga : PolymorphicEntityTestMedia<Manga>
    {
        public int? Volumes { get; set; }
    }

    public sealed class DirectAnime : PolymorphicEntityTestMedia
    {
        public int? Episodes { get; set; }
    }
}
