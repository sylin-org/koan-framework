using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Koan.Cache.Abstractions.Adapters;
using Microsoft.Extensions.DependencyModel;

namespace Koan.Cache.Adapters;

internal static class CacheAdapterResolver
{
    private static readonly ConcurrentDictionary<string, Type> RegistrarTypes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Assembly, byte> ProcessedAssemblies = new();
    private static readonly Lazy<IReadOnlyCollection<AssemblyName>> CandidateAssemblyNames = new(ResolveCandidateAssemblies);
    private static bool _bootstrapped;
    private static readonly object BootstrapLock = new();

    [RequiresUnreferencedCode("Cache adapter resolution reflects assemblies for registrar types.")]
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

    [RequiresUnreferencedCode("Cache adapter discovery reflects assemblies for registrar types.")]
    private static void EnsureBootstrap()
    {
        lock (BootstrapLock)
        {
            var interfaceType = typeof(ICacheAdapterRegistrar);

            LoadCandidateAssemblies(interfaceType);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                RegisterAssembly(assembly, interfaceType);
            }

            if (_bootstrapped)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => RegisterAssembly(args.LoadedAssembly, interfaceType);
            _bootstrapped = true;
        }
    }

    [RequiresUnreferencedCode("Cache adapter discovery reflects assemblies for registrar types.")]
    private static void LoadCandidateAssemblies([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type interfaceType)
    {
        foreach (var assemblyName in CandidateAssemblyNames.Value)
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                RegisterAssembly(assembly, interfaceType);
            }
            catch (FileNotFoundException)
            {
                // ignored - assembly not available in the current probing paths
            }
            catch (FileLoadException)
            {
                // ignored - dependency resolution failure
            }
            catch (BadImageFormatException)
            {
                // ignored - incompatible assembly format
            }
        }
    }

    [RequiresUnreferencedCode("Cache adapter discovery reflects assemblies for registrar types.")]
    private static void RegisterAssembly(Assembly assembly, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type interfaceType)
    {
        if (assembly.IsDynamic)
        {
            return;
        }

        if (!ProcessedAssemblies.TryAdd(assembly, 0))
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

    private static IReadOnlyCollection<AssemblyName> ResolveCandidateAssemblies()
    {
        var context = DependencyContext.Default;
        if (context is null)
        {
            return Array.Empty<AssemblyName>();
        }

        var assemblies = new HashSet<AssemblyName>(AssemblyNameComparer.Instance);

        foreach (var library in context.RuntimeLibraries)
        {
            if (!IsAdapterLibrary(library.Name))
            {
                continue;
            }

            foreach (var assemblyName in library.GetDefaultAssemblyNames(context))
            {
                assemblies.Add(assemblyName);
            }
        }

        return assemblies.ToArray();
    }

    private static bool IsAdapterLibrary(string libraryName)
        => libraryName.StartsWith("Koan.Cache.Adapter", StringComparison.OrdinalIgnoreCase);

    private sealed class AssemblyNameComparer : IEqualityComparer<AssemblyName>
    {
        public static readonly AssemblyNameComparer Instance = new();

        public bool Equals(AssemblyName? x, AssemblyName? y)
            => string.Equals(x?.FullName, y?.FullName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(AssemblyName obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FullName ?? obj.Name ?? string.Empty);
    }
}
