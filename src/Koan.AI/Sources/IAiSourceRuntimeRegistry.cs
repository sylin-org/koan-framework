using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources;

internal interface IAiSourceRuntimeRegistry
{
    AiSourceDefinition Apply(AiSourceDefinition source);
    bool SetEnabled(string name, bool enabled);
    bool Remove(string name, string? expectedOrigin);
    IReadOnlyCollection<AiSourceRuntimeSnapshot> GetRuntimeSources(bool includeDisabled = false);
    bool TrySetMemberHealth(
        string sourceName,
        long revision,
        string memberName,
        MemberHealthState state);
}

internal sealed record AiSourceRuntimeSnapshot(
    AiSourceDefinition Source,
    long Revision);
