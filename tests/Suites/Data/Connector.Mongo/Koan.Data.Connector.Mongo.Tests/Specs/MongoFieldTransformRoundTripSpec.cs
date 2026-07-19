using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// MongoDB derivation of the field-transform round-trip oracle (<see cref="FieldTransformRoundTrip"/>, ARCH-0098
/// §0 / ARCH-0079) — the classification substrate proven on the real Mongo adapter (the bare-store / non-Newtonsoft
/// realization). The transform runs ABOVE the adapter, so Mongo stores the already-wrapped value as a normal BSON
/// string and the read-reverse restores it: transformed at rest, original on read, caller's instance never
/// corrupted — the adapter-universality the AES-GCM classification transform relies on.
/// </summary>
public sealed class MongoFieldTransformRoundTripSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact(DisplayName = "Mongo: the field-transform round-trips — transformed at rest, restored on read")]
    public async Task Field_transform_round_trips()
    {
        RequireBackingStore();
        var contributor = new FieldTransformRoundTrip.Contributor();
        await using var host = await BootAsync(services => FieldTransformRoundTrip.Register(services, contributor));
        await FieldTransformRoundTrip.AssertRoundTripAsync(contributor);
    }
}
