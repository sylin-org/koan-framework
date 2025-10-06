using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Cache.Abstractions.Adapters;

namespace Koan.Cache.Adapters;

internal static class CacheAdapterResolver
{
    private static readonly ConcurrentDictionary<string, Type> RegistrarTypes = new(StringComparer.OrdinalIgnoreCase);
    private static bool _bootstrapped;
    private static readonly object BootstrapLock = new();

    public static ICacheAdapterRegistrar Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Adapter name must be provided.", nameof(name));
        }

        EnsureBootstrap();

        if (!RegistrarTypes.TryGetValue(name, out var type))
        {
            throw new InvalidOperationException($"Cache adapter '{name}' is not registered. Reference the appropriate Koan.Cache.Adapter package and ensure it implements ICacheAdapterRegistrar.");
        }

        return (ICacheAdapterRegistrar)Activator.CreateInstance(type)!;
    }

    private static void EnsureBootstrap()
    {
        if (_bootstrapped)
        {
            return;
        }

        lock (BootstrapLock)
        {
            if (_bootstrapped)
            {
                return;
            }

            var interfaceType = typeof(ICacheAdapterRegistrar);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                RegisterFromAssembly(assembly, interfaceType);
            }

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => RegisterFromAssembly(args.LoadedAssembly, interfaceType);
            _bootstrapped = true;
        }
    }

    private static void RegisterFromAssembly(Assembly assembly, Type interfaceType)
    {
        if (assembly.IsDynamic)
        {
            return;
        }

        IEnumerable<Type> candidates;
        try
        {
            candidates = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && interfaceType.IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException ex)
        {
            candidates = ex.Types.Where(t => t is not null && !t.IsAbstract && interfaceType.IsAssignableFrom(t!))!.Cast<Type>();
        }

        foreach (var candidate in candidates)
        {
            var registrar = (ICacheAdapterRegistrar?)Activator.CreateInstance(candidate);
            if (registrar is null)
            {
                continue;
            }

            RegistrarTypes.AddOrUpdate(registrar.Name, candidate, (_, _) => candidate);
        }
    }
}
