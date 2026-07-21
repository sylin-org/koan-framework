using AwesomeAssertions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Cross-adapter oracle for Data's host-owned stored-field transform seam. A generic reversible transform proves that
/// each real adapter persists the transformed value, reverses materialized values, and never mutates the caller's
/// instance. The contributor is registered through ordinary DI for the host under test.
/// </summary>
public static class FieldTransformRoundTrip
{
    private const string Prefix = "WRAP:";

    public sealed class TransformedDoc : Entity<TransformedDoc>
    {
        public string Secret { get; set; } = "";
        public string Plain { get; set; } = "";
    }

    public sealed class Contributor : IFieldTransformContributor
    {
        public string Id => "adapter-surface-wrap";
        public bool ReverseOnRead { get; set; } = true;

        public IFieldTransform? Build(Type entityType) =>
            entityType == typeof(TransformedDoc) ? new WrapTransform(this) : null;
    }

    public static void Register(IServiceCollection services, Contributor contributor) =>
        services.AddSingleton<IFieldTransformContributor>(contributor);

    public static async Task AssertRoundTripAsync(Contributor contributor)
    {
        using var partition = EntityContext.Partition("ftrt-" + Guid.CreateVersion7().ToString("n"));

        var doc = new TransformedDoc { Secret = "classified-value", Plain = "ordinary" };
        await doc.Save();
        doc.Secret.Should().Be("classified-value");

        var loaded = await TransformedDoc.Get(doc.Id);
        loaded.Should().NotBeNull();
        loaded!.Secret.Should().Be("classified-value");
        loaded.Plain.Should().Be("ordinary");

        contributor.ReverseOnRead = false;
        var raw = await TransformedDoc.Get(doc.Id);
        raw.Should().NotBeNull();
        raw!.Secret.Should().Be("WRAP:classified-value");

        contributor.ReverseOnRead = true;
        (await TransformedDoc.All()).Single().Secret.Should().Be("classified-value");
    }

    private sealed class WrapTransform(Contributor contributor) : IFieldTransform
    {
        public void ApplyOnWrite(object entity)
        {
            if (entity is TransformedDoc doc && !doc.Secret.StartsWith(Prefix, StringComparison.Ordinal))
                doc.Secret = Prefix + doc.Secret;
        }

        public void ApplyOnRead(object entity)
        {
            if (contributor.ReverseOnRead
                && entity is TransformedDoc doc
                && doc.Secret.StartsWith(Prefix, StringComparison.Ordinal))
                doc.Secret = doc.Secret[Prefix.Length..];
        }
    }
}
