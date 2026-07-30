namespace Koan.Data.Connector.Json.Tests.Specs;

public sealed class JsonOptionsSpec
{
    [Fact]
    public void Default_directory_is_platform_neutral()
    {
        new JsonDataOptions().DirectoryPath.Should().Be("data");
    }
}
