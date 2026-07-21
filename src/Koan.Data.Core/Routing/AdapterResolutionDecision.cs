using Koan.Data.Abstractions;
using Koan.Core.Providers;

namespace Koan.Data.Core.Routing;

internal sealed record AdapterResolutionDecision(
    IDataAdapterFactory Factory,
    string Source,
    ProviderSelectionReceipt Receipt)
{
    public string Adapter => Receipt.ProviderId;
    public string Via => Receipt.Reason;
    public int Priority => Receipt.Priority;
    public bool DirectIntent => Receipt.DirectIntent;
    public (string Adapter, string Source) ToTuple() => (Adapter, Source);
}
