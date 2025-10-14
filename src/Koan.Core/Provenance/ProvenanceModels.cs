using System;
using System.Collections.Generic;

namespace Koan.Core.Provenance
{
    public enum ProvenanceSettingSource
    {
        Unknown,
        Auto,
        AppSettings,
        Environment,
        LaunchKit,
        Custom
    }

    public enum ProvenanceSettingState
    {
        Unknown,
        Configured,
        Discovered,
        Default,
        Error
    }

    public enum ProvenanceNoteKind
    {
        Info,
        Warning,
        Error
    }

    public sealed record ProvenanceSetting(
        string Key,
        string? Value,
        bool IsSecret,
        ProvenanceSettingSource Source,
        string? SourceKey,
        IReadOnlyList<string> Consumers,
        ProvenanceSettingState State,
        DateTimeOffset UpdatedUtc);

    public sealed record ProvenanceTool(
        string Name,
        string Route,
        string? Description,
        string? Capability,
        DateTimeOffset UpdatedUtc);

    public sealed record ProvenanceNote(
        string Key,
        string Message,
        ProvenanceNoteKind Kind,
        DateTimeOffset UpdatedUtc);

    public sealed record ProvenanceModule(
        string PillarCode,
        string Name,
        string? Version,
        string? Description,
        string? Status,
        string? StatusDetail,
        DateTimeOffset RegisteredUtc,
        DateTimeOffset UpdatedUtc,
        IReadOnlyList<ProvenanceSetting> Settings,
        IReadOnlyList<ProvenanceTool> Tools,
        IReadOnlyList<ProvenanceNote> Notes);

    public sealed record ProvenancePillar(
        string Code,
        string Label,
        string ColorHex,
        string Icon,
        IReadOnlyList<ProvenanceModule> Modules);

    public sealed record ProvenanceSnapshot(
        long Sequence,
        Guid VersionId,
        DateTimeOffset CapturedUtc,
        IReadOnlyList<ProvenancePillar> Pillars)
    {
        public ProvenanceModule? FindModule(string pillarCode, string moduleName)
        {
            foreach (var pillar in Pillars)
            {
                if (!pillar.Code.Equals(pillarCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var module in pillar.Modules)
                {
                    if (module.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return module;
                    }
                }
            }

            return null;
        }
    }

    public sealed class ProvenanceSnapshotUpdatedEventArgs : EventArgs
    {
        public ProvenanceSnapshotUpdatedEventArgs(ProvenanceSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ProvenanceSnapshot Snapshot { get; }
    }
}
