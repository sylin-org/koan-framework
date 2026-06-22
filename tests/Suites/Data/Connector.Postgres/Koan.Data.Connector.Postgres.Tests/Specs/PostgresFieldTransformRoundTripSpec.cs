using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// Postgres derivation of the field-transform round-trip oracle (<see cref="FieldTransformRoundTrip"/>, ARCH-0098
/// §0 / ARCH-0079) — the classification substrate proven on the real Postgres adapter: the transform runs above
/// the adapter, so the already-wrapped value is stored in the jsonb envelope and the read-reverse restores it
/// (transformed at rest, original on read, caller untouched).
/// </summary>
public sealed class PostgresFieldTransformRoundTripSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact(DisplayName = "Postgres: the field-transform round-trips — transformed at rest, restored on read")]
    public async Task Field_transform_round_trips()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await FieldTransformRoundTrip.AssertRoundTripAsync();
    }
}
