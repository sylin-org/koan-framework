using System.Reflection;

namespace Koan.Orchestration.Aspire.Extensions;

/// <summary>
/// Helper class for discovering assemblies that contain Koan modules with Aspire resource contributions.
/// This discovery mechanism enables Koan's "Reference = Intent" philosophy by automatically finding
/// and registering resources from all referenced Koan modules.
/// </summary>
internal static class KoanAssemblyDiscovery
{
    /// <summary>
    /// Discover all assemblies that may contain Koan modules.
    /// </summary>
    /// <returns>A collection of assemblies that may contain Koan module registrars</returns>
    /// <remarks>
    /// The discovery process searches for assemblies using these criteria:
    /// 1. Assembly name starts with "Koan." (official Koan modules)
    /// 2. Assembly contains a concrete <see cref="Koan.Core.KoanModule"/> implementing
    ///    <see cref="IKoanAspireResources"/>
    /// 3. Assembly is currently loaded in the AppDomain
    ///
    /// This approach ensures that:
    /// - Only relevant assemblies are processed for performance
    /// - Custom modules following Koan patterns are discovered
    /// - The discovery is reliable across different deployment scenarios
    /// </remarks>
    public static IEnumerable<Assembly> GetKoanAssemblies()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var koanAssemblies = new List<Assembly>();

        foreach (var assembly in loadedAssemblies)
        {
            try
            {
                var assemblyName = assembly.GetName().Name;

                // Skip system assemblies and other non-Koan assemblies for performance
                if (string.IsNullOrEmpty(assemblyName) ||
                    assemblyName.StartsWith("System.") ||
                    assemblyName.StartsWith("Microsoft.") ||
                    assemblyName.StartsWith("Aspire.") ||
                    assemblyName.StartsWith("mscorlib") ||
                    assemblyName.StartsWith("netstandard"))
                {
                    continue;
                }

                // Check if this assembly is a Koan module
                if (IsKoanAssembly(assembly, assemblyName))
                {
                    koanAssemblies.Add(assembly);
                }
            }
            catch (Exception)
            {
                // Ignore assemblies that can't be processed (security, loading issues, etc.)
                // This ensures the discovery process is resilient to problematic assemblies
                continue;
            }
        }

        return koanAssemblies;
    }

    /// <summary>
    /// Determine if an assembly contains Koan modules.
    /// </summary>
    /// <param name="assembly">The assembly to check</param>
    /// <param name="assemblyName">The assembly name for performance optimization</param>
    /// <returns>True if the assembly appears to contain Koan modules</returns>
    private static bool IsKoanAssembly(Assembly assembly, string assemblyName)
    {
        // Fast path: Assembly name starts with "Koan."
        if (assemblyName.StartsWith("Koan.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Slower path: custom packages are admitted by the capability they implement,
        // not by a prescribed class name.
        try
        {
            return GetAspireResourceModuleTypes(assembly).Length > 0;
        }
        catch (ReflectionTypeLoadException)
        {
            // If we can't load types, assume it's not a Koan assembly
            return false;
        }
        catch (Exception)
        {
            // Other exceptions also suggest this isn't a Koan assembly
            return false;
        }
    }

    /// <summary>
    /// Get detailed information about discovered Koan assemblies for diagnostic purposes.
    /// </summary>
    /// <returns>A collection of assembly information including name, version, and registrar status</returns>
    /// <remarks>
    /// This method is useful for debugging and diagnostics when troubleshooting
    /// resource registration issues. It provides detailed information about what
    /// assemblies were discovered and why.
    /// </remarks>
    public static IEnumerable<KoanAssemblyInfo> GetDetailedAssemblyInfo()
    {
        var assemblies = GetKoanAssemblies();
        var assemblyInfos = new List<KoanAssemblyInfo>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var assemblyName = assembly.GetName();
                var hasAspireRegistrar = HasAspireRegistrar(assembly);

                assemblyInfos.Add(new KoanAssemblyInfo
                {
                    Name = assemblyName.Name ?? "Unknown",
                    Version = assemblyName.Version?.ToString() ?? "Unknown",
                    Location = assembly.Location,
                    HasKoanModule = HasKoanModule(assembly),
                    HasAspireRegistrar = hasAspireRegistrar,
                    AspireResourceModuleTypeName = GetAspireResourceModuleTypes(assembly).SingleOrDefault()?.FullName
                });
            }
            catch (Exception)
            {
                // Include assemblies that had discovery issues for diagnostic purposes
                assemblyInfos.Add(new KoanAssemblyInfo
                {
                    Name = assembly.GetName().Name ?? "Unknown",
                    Version = "Error",
                    Location = "Error loading assembly info",
                    HasKoanModule = false,
                    HasAspireRegistrar = false,
                    AspireResourceModuleTypeName = null
                });
            }
        }

        return assemblyInfos;
    }

    /// <summary>
    /// Check if an assembly contains a module that contributes Aspire resources.
    /// </summary>
    private static bool HasAspireRegistrar(Assembly assembly)
    {
        try
        {
            return GetAspireResourceModuleTypes(assembly).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if an assembly contains a concrete Koan module.
    /// </summary>
    private static bool HasKoanModule(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Any(static type =>
                !type.IsAbstract
                && !type.ContainsGenericParameters
                && typeof(Koan.Core.KoanModule).IsAssignableFrom(type));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the module that contributes Aspire resources, when present.
    /// </summary>
    internal static Type[] GetAspireResourceModuleTypes(Assembly assembly)
        => assembly.GetTypes()
            .Where(static type =>
                !type.IsAbstract
                && !type.ContainsGenericParameters
                && typeof(Koan.Core.KoanModule).IsAssignableFrom(type)
                && typeof(IKoanAspireResources).IsAssignableFrom(type))
            .ToArray();
}

/// <summary>
/// Information about a discovered Koan assembly for diagnostic purposes.
/// </summary>
public class KoanAssemblyInfo
{
    /// <summary>The assembly name</summary>
    public required string Name { get; init; }

    /// <summary>The assembly version</summary>
    public required string Version { get; init; }

    /// <summary>The assembly file location</summary>
    public required string Location { get; init; }

    /// <summary>Whether the assembly contains a concrete Koan module.</summary>
    public required bool HasKoanModule { get; init; }

    /// <summary>Whether a module contributes Aspire resources.</summary>
    public required bool HasAspireRegistrar { get; init; }

    /// <summary>The full type name of the Aspire resource module.</summary>
    public required string? AspireResourceModuleTypeName { get; init; }
}
