namespace Koan.Data.Connector.Json.Tests.Specs;

public sealed class JsonStorageLayoutOptionsSpec
{
    [Fact]
    public void Defaults_preserve_aggregate_layout_and_offer_only_semantic_layout_names()
    {
        var options = new JsonDataOptions();

        options.Layout.Should().Be(JsonStorageLayout.Aggregate);
        options.IndividualFilePath.Should().Be("{storage}/{id}.json");
        Enum.GetNames<JsonStorageLayout>().Should().Equal("Aggregate", "IndividualFiles");
    }
}
