using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Modules.Pillars;

namespace Koan.Core.Provenance
{
    public sealed class ProvenanceRegistry : IProvenanceRegistry
    {
    private static readonly Lazy<ProvenanceRegistry> _instance = new(() => new ProvenanceRegistry());

    public static ProvenanceRegistry Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly Dictionary<string, PillarState> _pillars = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;
    private ProvenanceSnapshot _snapshot = new(0, Guid.CreateVersion7(), DateTimeOffset.UtcNow, Array.Empty<ProvenancePillar>());

    private ProvenanceRegistry()
    {
        // Prime catalog with core pillar to avoid empty registry.
        CorePillarManifest.EnsureRegistered();
    }

    public ProvenanceModuleWriter GetOrCreateModule(string pillarCode, string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        if (string.IsNullOrWhiteSpace(pillarCode))
        {
            pillarCode = ResolvePillarCodeFromModule(moduleName) ?? CorePillarManifest.PillarCode;
        }

        lock (_gate)
        {
            if (!_pillars.TryGetValue(pillarCode, out var pillar))
            {
                pillar = new PillarState(ResolveDescriptor(pillarCode, moduleName));
                _pillars[pillar.Code] = pillar;
                UpdateSnapshotLocked();
            }

            if (!pillar.Modules.TryGetValue(moduleName, out var module))
            {
                module = new ModuleState(pillar.Code, moduleName);
                pillar.Modules[moduleName] = module;
                UpdateSnapshotLocked();
            }

            return new ProvenanceModuleWriter(this, pillar, module);
        }
    }

    public ProvenanceSnapshot CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<ProvenanceSnapshotUpdatedEventArgs>? SnapshotUpdated;

    internal void TouchModule(PillarState pillar, ModuleState module)
    {
        lock (_gate)
        {
            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    internal void UpdateModuleMetadata(PillarState pillar, ModuleState module, string? version, string? description)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                module.Version = version.Trim();
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                module.Description = description.Trim();
            }

            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    internal void UpdateModuleStatus(PillarState pillar, ModuleState module, string? status, string? detail)
    {
        lock (_gate)
        {
            module.Status = status;
            module.StatusDetail = detail;
            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    internal void ReplaceSetting(PillarState pillar, ModuleState module, SettingState setting)
    {
        lock (_gate)
        {
            module.Settings[setting.Key] = setting;
            module.UpdatedUtc = setting.UpdatedUtc;
            UpdateSnapshotLocked();
        }
    }

    internal void RemoveSetting(PillarState pillar, ModuleState module, string key)
    {
        lock (_gate)
        {
            module.Settings.Remove(key);
            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    internal void ReplaceTool(PillarState pillar, ModuleState module, ToolState tool)
    {
        lock (_gate)
        {
            module.Tools[tool.Name] = tool;
            module.UpdatedUtc = tool.UpdatedUtc;
            UpdateSnapshotLocked();
        }
    }

    internal void RemoveTool(PillarState pillar, ModuleState module, string name)
    {
        lock (_gate)
        {
            module.Tools.Remove(name);
            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    internal void ReplaceNote(PillarState pillar, ModuleState module, NoteState note)
    {
        lock (_gate)
        {
            module.Notes[note.Key] = note;
            module.UpdatedUtc = note.UpdatedUtc;
            UpdateSnapshotLocked();
        }
    }

    internal void RemoveNote(PillarState pillar, ModuleState module, string key)
    {
        lock (_gate)
        {
            module.Notes.Remove(key);
            module.UpdatedUtc = DateTimeOffset.UtcNow;
            UpdateSnapshotLocked();
        }
    }

    private void UpdateSnapshotLocked()
    {
        var pillars = _pillars.Values
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(pillar => pillar.ToSnapshot())
            .ToArray();

        _sequence++;
        var snapshot = new ProvenanceSnapshot(
            _sequence,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            pillars);

        _snapshot = snapshot;
        SnapshotUpdated?.Invoke(this, new ProvenanceSnapshotUpdatedEventArgs(snapshot));
    }

    private static string? ResolvePillarCodeFromModule(string moduleName)
    {
        if (KoanPillarCatalog.TryMatchByModuleName(moduleName, out var descriptor))
        {
            return descriptor.Code;
        }

        return null;
    }

    private static KoanPillarCatalog.PillarDescriptor ResolveDescriptor(string pillarCode, string moduleName)
    {
        if (KoanPillarCatalog.TryGetByCode(pillarCode, out var descriptor))
        {
            return descriptor;
        }

        if (KoanPillarCatalog.TryMatchByModuleName(moduleName, out var byNamespace))
        {
            return byNamespace;
        }

        var label = pillarCode switch
        {
            null or "" => "General",
            _ => char.ToUpperInvariant(pillarCode[0]) + pillarCode[1..]
        };

        var fallback = new KoanPillarCatalog.PillarDescriptor(pillarCode ?? CorePillarManifest.PillarCode, label, "#2563eb", "📦");
        try
        {
            KoanPillarCatalog.RegisterDescriptor(fallback);
        }
        catch
        {
            // Ignore registration failures (e.g., concurrent registration).
        }

        if (KoanPillarCatalog.TryGetByCode(pillarCode, out var registered))
        {
            return registered;
        }

        return fallback;
    }

    internal sealed class PillarState
    {
        public PillarState(KoanPillarCatalog.PillarDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Code = descriptor.Code;
        }

        public string Code { get; }
        public KoanPillarCatalog.PillarDescriptor Descriptor { get; private set; }
        public Dictionary<string, ModuleState> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ProvenancePillar ToSnapshot()
        {
            var modules = Modules.Values
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.ToSnapshot())
                .ToArray();

            return new ProvenancePillar(Code, Descriptor.Label, Descriptor.ColorHex, Descriptor.Icon, modules);
        }
    }

    internal sealed class ModuleState
    {
        public ModuleState(string pillarCode, string name)
        {
            PillarCode = pillarCode;
            Name = name;
            RegisteredUtc = DateTimeOffset.UtcNow;
            UpdatedUtc = RegisteredUtc;
        }

        public string PillarCode { get; }
        public string Name { get; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? StatusDetail { get; set; }
        public DateTimeOffset RegisteredUtc { get; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public Dictionary<string, SettingState> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ToolState> Tools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, NoteState> Notes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ProvenanceModule ToSnapshot()
        {
            var settings = Settings.Values
                .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.ToSnapshot())
                .ToArray();
            var tools = Tools.Values
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.ToSnapshot())
                .ToArray();
            var notes = Notes.Values
                .OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase)
                .Select(n => n.ToSnapshot())
                .ToArray();

            return new ProvenanceModule(
                PillarCode,
                Name,
                Version,
                Description,
                Status,
                StatusDetail,
                RegisteredUtc,
                UpdatedUtc,
                settings,
                tools,
                notes);
        }
    }

    internal sealed record SettingState(
        string Key,
        string Label,
        string Description,
        string? Value,
        bool IsSecret,
        ProvenanceSettingSource Source,
        string? SourceKey,
        IReadOnlyList<string> Consumers,
        ProvenanceSettingState State,
        DateTimeOffset UpdatedUtc)
    {
        public ProvenanceSetting ToSnapshot()
            => new(Key, Label, Description, Value, IsSecret, Source, SourceKey, Consumers, State, UpdatedUtc);
    }

    internal sealed record ToolState(
        string Name,
        string Route,
        string? Description,
        string? Capability,
        DateTimeOffset UpdatedUtc)
    {
        public ProvenanceTool ToSnapshot()
            => new(Name, Route, Description, Capability, UpdatedUtc);
    }

    internal sealed record NoteState(
        string Key,
        string Message,
        ProvenanceNoteKind Kind,
        DateTimeOffset UpdatedUtc)
    {
        public ProvenanceNote ToSnapshot()
            => new(Key, Message, Kind, UpdatedUtc);
    }
    }
}
