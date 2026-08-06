namespace Koan.Data.Core;

/// <summary>Host-owned Data declaration root.</summary>
public sealed class DataCompositionBuilder
{
    internal DataCompositionBuilder() { }

    public DataSourceBuilder Source(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var source = name.Trim();
        DataOperationCatalog.DeclareSource(source);
        return new DataSourceBuilder(source);
    }
}
