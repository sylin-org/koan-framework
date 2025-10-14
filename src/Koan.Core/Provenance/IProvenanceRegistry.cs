using System;

namespace Koan.Core.Provenance
{
    public interface IProvenanceRegistry
    {
        ProvenanceModuleWriter GetOrCreateModule(string pillarCode, string moduleName);
        ProvenanceSnapshot CurrentSnapshot { get; }
        event EventHandler<ProvenanceSnapshotUpdatedEventArgs>? SnapshotUpdated;
    }
}
