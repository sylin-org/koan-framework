using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Hygiene;

/// <summary>Builds the hygiene transform once for each Entity type that carries hygiene attributes.</summary>
internal sealed class HygieneFieldTransformContributor : IFieldTransformContributor
{
    public string Id => "koan.hygiene";

    public int Order => 200;

    public IFieldTransform? Build(Type entityType)
    {
        var bag = new HygienePropertyBag(entityType);
        return bag.HasHygiene ? new HygieneFieldTransform(bag) : null;
    }
}
