using System;
using System.Collections.Generic;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector;

public static class VectorProfiles
{
    private static readonly object Sync = new();
    private static readonly List<Action<VectorProfileCollection>> Registered = new();
    private static VectorWorkflowRegistry.IRegistrar? _registrar;

    public static void Register(Action<VectorProfileCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        lock (Sync)
        {
            Registered.Add(configure);
            if (_registrar is not null)
            {
                configure(new VectorProfileCollection(_registrar));
            }
        }
    }

    internal static void Attach(VectorWorkflowRegistry.IRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);

        lock (Sync)
        {
            _registrar = registrar;
            foreach (var action in Registered)
            {
                action(new VectorProfileCollection(registrar));
            }
        }
    }

    public sealed class VectorProfileCollection
    {
        private readonly VectorWorkflowRegistry.IRegistrar _registrar;

        internal VectorProfileCollection(VectorWorkflowRegistry.IRegistrar registrar)
        {
            _registrar = registrar;
        }

        public VectorProfileBuilder<TEntity> For<TEntity>(string profileName)
            where TEntity : class, IEntity<string>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
            return new VectorProfileBuilder<TEntity>(_registrar, profileName);
        }
    }

    public sealed class VectorProfileBuilder<TEntity>
        where TEntity : class, IEntity<string>
    {
        private readonly VectorWorkflowRegistry.IRegistrar _registrar;
        private readonly string _profileName;

        internal VectorProfileBuilder(VectorWorkflowRegistry.IRegistrar registrar, string profileName)
        {
            _registrar = registrar;
            _profileName = profileName;
        }

        public VectorProfileBuilder<TEntity> TopK(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "topK must be greater than zero.");
            }

            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg => cfg.TopK = value);
            return this;
        }

        public VectorProfileBuilder<TEntity> Alpha(double value)
        {
            if (value is < 0d or > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "alpha must be between 0.0 and 1.0.");
            }

            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg => cfg.Alpha = value);
            return this;
        }

        public VectorProfileBuilder<TEntity> VectorName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg => cfg.VectorName = name);
            return this;
        }

        public VectorProfileBuilder<TEntity> EmitMetrics(bool enable = true)
        {
            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg => cfg.EmitMetrics = enable);
            return this;
        }

        public VectorProfileBuilder<TEntity> WithMetadata(string key, object? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg => cfg.Metadata[key] = value);
            return this;
        }

        public VectorProfileBuilder<TEntity> WithMetadata(IDictionary<string, object?> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg =>
            {
                foreach (var entry in values)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                    cfg.Metadata[entry.Key] = entry.Value;
                }
            });

            return this;
        }

        public VectorProfileBuilder<TEntity> WithMetadata(Action<IDictionary<string, object?>> enrich)
        {
            ArgumentNullException.ThrowIfNull(enrich);

            _registrar.ConfigureProfile(typeof(TEntity), _profileName, cfg =>
            {
                cfg.MetadataInitializers.Add(enrich);
            });

            return this;
        }
    }
}
