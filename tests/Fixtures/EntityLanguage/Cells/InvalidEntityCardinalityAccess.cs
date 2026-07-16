using Koan.Data.Core.Selection;

public static class InvalidEntityCardinalityAccessConsumer
{
    public static IAsyncEnumerable<string> Read(IEnumerable<string> values)
        => EntityCardinality.Many(values);
}
