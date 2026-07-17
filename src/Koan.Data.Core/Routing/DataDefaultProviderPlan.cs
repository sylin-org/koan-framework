using System.ComponentModel;
using Koan.Core.Providers;

namespace Koan.Data.Core.Routing;

/// <summary>One host-owned default Data decision shared by runtime construction and composition reporting.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DataDefaultProviderPlan
{
    public DataDefaultProviderPlan(DataProviderCatalog providers, DataSourceRegistry sources)
    {
        var source = sources.GetSource("Default");
        if (source is not null && !string.IsNullOrWhiteSpace(source.Adapter))
        {
            var selected = providers.Require(
                source.Adapter,
                "data:default",
                Infrastructure.Constants.Diagnostics.Reasons.DefaultSource,
                "Correct Koan:Data:Sources:Default:Adapter or reference the connector package that owns the requested adapter.");
            Decision = new AdapterResolutionDecision(selected.Factory, "Default", selected.Receipt);
            return;
        }

        var automatic = providers.SelectAutomatic();
        Decision = new AdapterResolutionDecision(automatic.Factory, "Default", automatic.Receipt);
    }

    public string ProviderId => Receipt.ProviderId;
    public ProviderSelectionReceipt Receipt => Decision.Receipt;
    internal AdapterResolutionDecision Decision { get; }
}
