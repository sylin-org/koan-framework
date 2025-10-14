using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core;

namespace Koan.Core.Provenance
{
    public sealed class ProvenanceModuleWriter
    {
        private readonly ProvenanceRegistry _registry;
        private readonly ProvenanceRegistry.PillarState _pillar;
        private readonly ProvenanceRegistry.ModuleState _module;

        internal ProvenanceModuleWriter(ProvenanceRegistry registry, ProvenanceRegistry.PillarState pillar, ProvenanceRegistry.ModuleState module)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pillar = pillar ?? throw new ArgumentNullException(nameof(pillar));
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public string PillarCode => _module.PillarCode;
        public string ModuleName => _module.Name;

        public ProvenanceModuleWriter Describe(string? version = null, string? description = null)
        {
            _registry.UpdateModuleMetadata(_pillar, _module, version, description);
            return this;
        }

        public ProvenanceModuleWriter SetStatus(string status, string? detail = null)
        {
            _registry.UpdateModuleStatus(_pillar, _module, status, detail);
            return this;
        }

        public ProvenanceModuleWriter ClearStatus()
        {
            _registry.UpdateModuleStatus(_pillar, _module, null, null);
            return this;
        }

        public ProvenanceModuleWriter SetSetting(string key, Action<ProvenanceSettingBuilder> configure)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new ProvenanceSettingBuilder(key);
            configure(builder);
            var compiled = builder.Build(DateTimeOffset.UtcNow);

            _registry.ReplaceSetting(_pillar, _module, compiled);
            return this;
        }

        public ProvenanceModuleWriter RemoveSetting(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _registry.RemoveSetting(_pillar, _module, key);
            return this;
        }

        public ProvenanceModuleWriter SetTool(string name, Action<ProvenanceToolBuilder> configure)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new ProvenanceToolBuilder(name);
            configure(builder);
            var compiled = builder.Build(DateTimeOffset.UtcNow);

            _registry.ReplaceTool(_pillar, _module, compiled);
            return this;
        }

        public ProvenanceModuleWriter RemoveTool(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            _registry.RemoveTool(_pillar, _module, name);
            return this;
        }

        public ProvenanceModuleWriter SetNote(string key, Action<ProvenanceNoteBuilder> configure)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new ProvenanceNoteBuilder(key);
            configure(builder);
            var compiled = builder.Build(DateTimeOffset.UtcNow);

            _registry.ReplaceNote(_pillar, _module, compiled);
            return this;
        }

        public ProvenanceModuleWriter RemoveNote(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _registry.RemoveNote(_pillar, _module, key);
            return this;
        }
    }

    public sealed class ProvenanceSettingBuilder
    {
        private readonly string _key;
        private string? _value;
        private bool _isSecret;
        private Func<string?, string>? _redactor;
        private ProvenanceSettingSource _source = ProvenanceSettingSource.Unknown;
        private string? _sourceKey;
        private readonly HashSet<string> _consumers = new(StringComparer.OrdinalIgnoreCase);
        private ProvenanceSettingState _state = ProvenanceSettingState.Unknown;

        internal ProvenanceSettingBuilder(string key)
        {
            _key = key;
        }

        public ProvenanceSettingBuilder Value(string? value)
        {
            _value = value;
            return this;
        }

        public ProvenanceSettingBuilder Secret(Func<string?, string>? redactor = null)
        {
            _isSecret = true;
            _redactor = redactor;
            return this;
        }

        public ProvenanceSettingBuilder Source(ProvenanceSettingSource source, string? sourceKey = null)
        {
            _source = source;
            _sourceKey = sourceKey;
            return this;
        }

        public ProvenanceSettingBuilder Consumers(params string[] consumers)
        {
            if (consumers is null)
            {
                return this;
            }

            foreach (var consumer in consumers)
            {
                if (!string.IsNullOrWhiteSpace(consumer))
                {
                    _consumers.Add(consumer);
                }
            }

            return this;
        }

        public ProvenanceSettingBuilder State(ProvenanceSettingState state)
        {
            _state = state;
            return this;
        }

        internal ProvenanceRegistry.SettingState Build(DateTimeOffset timestamp)
        {
            var consumers = _consumers.Count == 0
                ? Array.Empty<string>()
                : _consumers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();

            var displayValue = _isSecret
                ? (_redactor?.Invoke(_value) ?? Redaction.DeIdentify(_value ?? string.Empty))
                : _value;

            return new ProvenanceRegistry.SettingState(
                _key,
                displayValue,
                _isSecret,
                _source,
                _sourceKey,
                consumers,
                _state,
                timestamp);
        }
    }

    public sealed class ProvenanceToolBuilder
    {
        private readonly string _name;
        private string? _route = string.Empty;
        private string? _description;
        private string? _capability;

        internal ProvenanceToolBuilder(string name)
        {
            _name = name;
        }

        public ProvenanceToolBuilder Route(string route)
        {
            _route = route;
            return this;
        }

        public ProvenanceToolBuilder Description(string? description)
        {
            _description = description;
            return this;
        }

        public ProvenanceToolBuilder Capability(string? capability)
        {
            _capability = capability;
            return this;
        }

        internal ProvenanceRegistry.ToolState Build(DateTimeOffset timestamp)
        {
            var route = string.IsNullOrWhiteSpace(_route) ? string.Empty : _route;
            return new ProvenanceRegistry.ToolState(_name, route, _description, _capability, timestamp);
        }
    }

    public sealed class ProvenanceNoteBuilder
    {
        private readonly string _key;
        private string _message = string.Empty;
        private ProvenanceNoteKind _kind = ProvenanceNoteKind.Info;

        internal ProvenanceNoteBuilder(string key)
        {
            _key = key;
        }

        public ProvenanceNoteBuilder Message(string message)
        {
            _message = message ?? string.Empty;
            return this;
        }

        public ProvenanceNoteBuilder Kind(ProvenanceNoteKind kind)
        {
            _kind = kind;
            return this;
        }

        internal ProvenanceRegistry.NoteState Build(DateTimeOffset timestamp)
        {
            return new ProvenanceRegistry.NoteState(_key, _message, _kind, timestamp);
        }
    }
}
