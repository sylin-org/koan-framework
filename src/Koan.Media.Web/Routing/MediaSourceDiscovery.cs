using System.Reflection;
using Koan.Core.Hosting.Bootstrap;
using Koan.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Media.Web.Routing;

/// <summary>Compiles the default media source choice from the application's concrete media Entities.</summary>
internal static class MediaSourceDiscovery
{
    internal sealed record Selection(int CandidateCount, string Summary);

    public static Selection RegisterDefault(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var candidates = SafeGetTypes(AssemblyCache.Instance.GetAllAssemblies())
            .Where(IsConcreteMediaEntity)
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        return RegisterDefault(services, candidates);
    }

    internal static Selection RegisterDefault(IServiceCollection services, IReadOnlyList<Type> candidates)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidates);

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IMediaSource)))
        {
            return new Selection(candidates.Count,
                $"Explicit IMediaSource; {candidates.Count} concrete MediaEntity candidate(s) discovered.");
        }

        if (candidates.Count == 1)
        {
            var implementation = typeof(MediaEntitySource<>).MakeGenericType(candidates[0]);
            services.TryAdd(ServiceDescriptor.Singleton(typeof(IMediaSource), implementation));
            return new Selection(1, $"Automatic Entity source: {candidates[0].FullName}.");
        }

        var candidateNames = candidates.Count == 0
            ? "none"
            : string.Join(", ", candidates.Select(static type => type.FullName ?? type.Name));
        var correction = candidates.Count == 0
            ? "Define one MediaEntity<T>, register a custom IMediaSource, or remove the Media Web reference."
            : "Select the source explicitly with services.AddMediaSource<T>() or register a custom IMediaSource.";
        var message = $"Koan Media Web cannot select a default media source: discovered {candidates.Count} " +
                      $"concrete MediaEntity candidate(s) ({candidateNames}). {correction}";

        // This fallback is deliberately lazy so an application module that runs later can replace it with an
        // explicit source. MediaWebModule.Start resolves the final registration and turns an unresolved choice into
        // a host-start correction rather than a generic controller-activation failure on the first request.
        services.TryAddSingleton<IMediaSource>(_ => throw new InvalidOperationException(message));
        return new Selection(candidates.Count, message);
    }

    private static bool IsConcreteMediaEntity(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition) return false;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType || current.GetGenericTypeDefinition() != typeof(MediaEntity<>)) continue;
            return current.GetGenericArguments()[0] == type;
        }

        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is not null) yield return type;
            }
        }
    }
}
