using Koan.AI.Contracts.Adapters;

namespace Koan.AI.Resolution;

public sealed class AmbiguousAdapterException : InvalidOperationException
{
    public string Capability { get; }
    public IReadOnlyList<string> AdapterIds { get; }

    public AmbiguousAdapterException(string capability, IReadOnlyList<IAiAdapter> adapters)
        : base($"Multiple adapters have '{capability}' capability: " +
               $"[{string.Join(", ", adapters.Select(a => a.Id))}]. " +
               $"Specify the target explicitly (e.g., to: \"{adapters[0].Id}\").")
    {
        Capability = capability;
        AdapterIds = adapters.Select(a => a.Id).ToList();
    }
}
