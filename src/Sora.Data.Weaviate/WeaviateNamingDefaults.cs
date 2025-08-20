using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Weaviate;

public sealed class WeaviateNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Kind => "weaviate";
    public string FormatSetName(string baseName) => baseName.Replace('.', '_');
}
