using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Modules.Pillars;
using Koan.Core.Provenance;

namespace Koan.Core.Hosting.Bootstrap;

// Greenfield bootstrap report collected from module registrars.
public sealed class BootReport
{
    private readonly List<BootModuleBuilder> _modules = new();
    private readonly IProvenanceRegistry _registry;
    private readonly List<ProvenanceModuleWriter?> _writers = new();
    private ProvenanceModuleWriter? _currentWriter;

    public BootReport()
        : this(ProvenanceRegistry.Instance)
    {
    }

    public BootReport(IProvenanceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void AddModule(string name, string? version = null, string? description = null)
    {
        var pillarCode = ResolvePillarForModule(name);
        _currentWriter = _registry.GetOrCreateModule(pillarCode, name);
        _currentWriter.Describe(version, description);

        var builder = new BootModuleBuilder(name, version) { Description = description };
        _modules.Add(builder);
        _writers.Add(_currentWriter);
    }

    public void AddSetting(
        string key,
        string? value,
        bool isSecret = false,
        BootSettingSource source = BootSettingSource.Unknown,
        IReadOnlyCollection<string>? consumers = null,
        string? sourceKey = null)
    {
        if (_modules.Count == 0) return;

        var current = _modules[^1];
        var sanitized = value ?? "(null)";
        if (isSecret)
        {
            sanitized = Koan.Core.Redaction.DeIdentify(sanitized);
        }

        current.Settings.Add(new BootModuleSettingEntry(
            key,
            sanitized,
            isSecret,
            source,
            sourceKey ?? string.Empty,
            consumers is null ? Array.Empty<string>() : consumers.ToArray()));

        _currentWriter ??= _writers[^1];
        _currentWriter?.SetSetting(key, builder =>
        {
            builder.Value(value)
                .State(ProvenanceSettingState.Configured)
                .Source(MapSource(source), sourceKey);

            if (consumers is not null && consumers.Count > 0)
            {
                builder.Consumers(consumers.ToArray());
            }

            if (isSecret)
            {
                builder.Secret(input => Koan.Core.Redaction.DeIdentify(input ?? string.Empty));
            }
        });
    }

    public void AddNote(string message)
    {
        if (_modules.Count == 0) return;
        var current = _modules[^1];
        current.Notes.Add(message);

        _currentWriter ??= _writers[^1];
        var key = $"note.{current.Notes.Count:000}";
        _currentWriter?.SetNote(key, builder =>
        {
            builder.Message(message ?? string.Empty)
                .Kind(ProvenanceNoteKind.Info);
        });
    }

    public void AddTool(string name, string route, string? description = null, string? capability = null)
    {
        if (_modules.Count == 0) return;

        _modules[^1].Tools.Add(new BootModuleToolEntry(name, route, description, capability));

        _currentWriter ??= _writers[^1];
        _currentWriter?.SetTool(name, builder =>
        {
            builder.Route(route)
                .Description(description)
                .Capability(capability);
        });
    }

    public IReadOnlyList<BootModule> GetModules()
        => _modules
            .Select(m => new BootModule(
                m.Name,
                m.Version,
                m.Description,
                m.Settings.Count == 0 && m.Notes.Count == 0 && m.Tools.Count == 0,
                m.Settings.Select(s => new BootModuleSetting(s.Key, s.Value, s.Secret, s.Source, s.SourceKey, s.Consumers)).ToList(),
                m.Notes.AsReadOnly(),
                m.Tools.Select(t => new BootModuleTool(t.Name, t.Route, t.Description, t.Capability)).ToList()))
            .ToList();

    private static string ResolvePillarForModule(string moduleName)
    {
        if (KoanPillarCatalog.TryMatchByModuleName(moduleName, out var descriptor))
        {
            return descriptor.Code;
        }

        return CorePillarManifest.PillarCode;
    }

    private static ProvenanceSettingSource MapSource(BootSettingSource source)
        => source switch
        {
            BootSettingSource.Auto => ProvenanceSettingSource.Auto,
            BootSettingSource.AppSettings => ProvenanceSettingSource.AppSettings,
            BootSettingSource.Environment => ProvenanceSettingSource.Environment,
            BootSettingSource.LaunchKit => ProvenanceSettingSource.LaunchKit,
            BootSettingSource.Custom => ProvenanceSettingSource.Custom,
            _ => ProvenanceSettingSource.Unknown
        };

}

public enum BootSettingSource
{
    Unknown,
    Auto,
    AppSettings,
    Environment,
    LaunchKit,
    Custom
}

public sealed record BootModule(
    string Name,
    string? Version,
    string? Description,
    bool IsStub,
    IReadOnlyList<BootModuleSetting> Settings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<BootModuleTool> Tools);

public sealed record BootModuleSetting(
    string Key,
    string Value,
    bool Secret,
    BootSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers);

public sealed record BootModuleTool(
    string Name,
    string Route,
    string? Description,
    string? Capability);

internal sealed class BootModuleBuilder
{
    public BootModuleBuilder(string name, string? version)
    {
        Name = name;
        Version = version;
    }

    public string Name { get; }
    public string? Version { get; }
    public string? Description { get; set; }
    public List<BootModuleSettingEntry> Settings { get; } = new();
    public List<string> Notes { get; } = new();
    public List<BootModuleToolEntry> Tools { get; } = new();
}

internal sealed record BootModuleSettingEntry(
    string Key,
    string Value,
    bool Secret,
    BootSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers);

internal sealed record BootModuleToolEntry(
    string Name,
    string Route,
    string? Description,
    string? Capability);
