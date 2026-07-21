using Koan.Classification.Crypto;
using Koan.Classification.Infrastructure;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Classification.Pipeline;

/// <summary>Builds the Classification transform once for each applicable Entity type in the current host.</summary>
internal sealed class ClassificationFieldTransformContributor(
    IFieldCipher cipher,
    IClassificationKeyProvider keys,
    SegmentationPlan segmentation) : IFieldTransformContributor
{
    public string Id => Constants.Pipeline.ContributorId;

    public int Order => Constants.Pipeline.Order;

    public IFieldTransform? Build(Type entityType)
    {
        var bag = new ClassifiedPropertyBag(entityType);
        return bag.HasClassifiedFields
            ? new ClassificationFieldTransform(cipher, keys, segmentation.For(entityType), bag)
            : null;
    }
}
