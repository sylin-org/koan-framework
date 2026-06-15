using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Core;

/// <summary>
/// Scan-based DI registration for "many implementations of one service contract" shapes
/// (adapters, plug-ins, scheduled tasks, etc.). Lets <see cref="IKoanAutoRegistrar.Initialize"/>
/// register an entire family of implementations without listing each concrete type by hand —
/// adding a new implementation becomes a zero-config drop.
/// </summary>
/// <remarks>
/// <para>
/// Companion to <see cref="ServiceCollectionExtensions"/> (the framework's own
/// <c>AddKoan</c>/<c>AddKoanCore</c>) and <see cref="ServiceCollectionDecoratorExtensions"/>
/// (the decorator helper). All three live at the <c>Koan.Core</c> root because they are
/// general-purpose DI utilities a consumer uses from inside an auto-registrar.
/// </para>
/// </remarks>
public static class ServiceCollectionScanExtensions
{
    /// <summary>
    /// Registers every concrete type implementing <typeparamref name="TService"/> found in the
    /// supplied assemblies as a singleton <typeparamref name="TService"/>. Uses
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// so accidental double-registration (two auto-registrars scanning the same assembly) is a
    /// no-op instead of producing duplicate instances when an <see cref="IEnumerable{T}"/> is
    /// resolved.
    /// </summary>
    /// <typeparam name="TService">Service contract — typically an interface that has many
    /// concrete implementations consumed as an enumerable (e.g. crawler sources, extractors,
    /// scheduled tasks, download proxies, taxonomy distributors).</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="assemblies">Assemblies to scan. Pass <c>typeof(SomeMarker).Assembly</c> at
    /// the call site; defaults to <see cref="Assembly.GetCallingAssembly"/> when none supplied.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// // Inside an IKoanAutoRegistrar.Initialize(services) implementation:
    /// services.AddAllOf&lt;IPackageSource&gt;(typeof(KoanAutoRegistrar).Assembly);
    /// services.AddAllOf&lt;IDownloadProxy&gt;(typeof(KoanAutoRegistrar).Assembly);
    /// </code>
    /// </example>
    public static IServiceCollection AddAllOf<TService>(
        this IServiceCollection services,
        params Assembly[] assemblies)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }
        return services.AddAllOf(typeof(TService), assemblies);
    }

    /// <summary>
    /// Non-generic counterpart to <see cref="AddAllOf{TService}(IServiceCollection, Assembly[])"/>
    /// for callers that resolve the contract type dynamically (configuration-driven module
    /// composition, integration tests that parameterise the service type, etc.).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="serviceType">Service contract type. Must not be sealed or a value type.</param>
    /// <param name="assemblies">Assemblies to scan.</param>
    public static IServiceCollection AddAllOf(
        this IServiceCollection services,
        Type serviceType,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceType);
        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial type-load failures (third-party plugin assembly missing a transient
                // dep, e.g.) shouldn't kill discovery for everything else. Take what we got.
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var t in types)
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!serviceType.IsAssignableFrom(t)) continue;
                // Explicit opt-out: parameterized classes (per-instance ctor args that DI can't
                // resolve) mark themselves with [NotAutoRegistered] so the scan skips them. Without
                // this signal we'd try to AddSingleton them and the service-provider validation
                // step would fail loudly at app startup.
                if (t.GetCustomAttributes(typeof(NotAutoRegisteredAttribute), inherit: false).Length > 0) continue;
                services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, t));
            }
        }
        return services;
    }
}
