using System.Text.Json.Serialization.Metadata;

namespace Koan.Web.Json.Strict.Options;

public sealed class KoanMinimalJsonOptions
{
    public bool Strict { get; set; }

    public bool AllowDuplicateProperties { get; set; } = false;

    public bool CombineRegisteredResolvers { get; set; } = true;

    public IJsonTypeInfoResolver? TypeInfoResolver { get; set; }
        = null;
}
