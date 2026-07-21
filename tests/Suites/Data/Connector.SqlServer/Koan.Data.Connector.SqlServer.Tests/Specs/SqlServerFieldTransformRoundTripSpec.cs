using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// SQL Server derivation of the field-transform round-trip oracle (<see cref="FieldTransformRoundTrip"/>, ARCH-0098
/// §0 / ARCH-0079) — the classification substrate proven on the real SQL Server adapter: the transform runs above
/// the adapter, so the already-wrapped value is stored in the [Json] envelope and the read-reverse restores it
/// (transformed at rest, original on read, caller untouched).
/// </summary>
public sealed class SqlServerFieldTransformRoundTripSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SqlServer: the field-transform round-trips — transformed at rest, restored on read")]
    public async Task Field_transform_round_trips()
    {
        RequireBackingStore();
        var contributor = new FieldTransformRoundTrip.Contributor();
        await using var host = await BootAsync(services => FieldTransformRoundTrip.Register(services, contributor));
        await FieldTransformRoundTrip.AssertRoundTripAsync(contributor);
    }
}
