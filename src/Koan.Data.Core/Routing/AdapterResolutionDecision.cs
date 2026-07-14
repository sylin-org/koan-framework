namespace Koan.Data.Core.Routing;

internal sealed record AdapterResolutionDecision(
    string Adapter,
    string Source,
    string Via,
    int? Priority = null)
{
    public (string Adapter, string Source) ToTuple() => (Adapter, Source);
}
