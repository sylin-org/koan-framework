namespace Sora.Core;

internal interface IHealthRegistry
{
    void Add(IHealthContributor contributor);
    IReadOnlyCollection<IHealthContributor> All { get; }
}