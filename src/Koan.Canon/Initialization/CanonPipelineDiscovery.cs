using System.Reflection;
using Koan.Core.Hosting.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon;

/// <summary>
/// Compiles source-discovered contributors into deterministic per-model pipelines once per host.
/// </summary>
internal static class CanonPipelineDiscovery
{
    private static readonly MethodInfo ConfigureModelMethod = typeof(CanonPipelineDiscovery)
        .GetMethod(nameof(ConfigureModel), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IReadOnlyList<Type> DiscoverContributorTypes() => KoanRegistry
        .GetDiscoveredImplementors(typeof(ICanonPipelineContributor))
        .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
        .OrderBy(static type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    public static void Register(IServiceCollection services)
    {
        var contributorTypes = DiscoverContributorTypes();
        services.TryAddSingleton(new CanonContributorCatalog(contributorTypes));

        foreach (var contributorType in contributorTypes)
        {
            services.TryAddSingleton(contributorType);
        }
    }

    public static void Configure(CanonRuntimeBuilder builder, IServiceProvider services)
    {
        var bindings = services.GetRequiredService<CanonContributorCatalog>().ContributorTypes
            .SelectMany(static type => type.GetInterfaces()
                .Where(static contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(ICanonPipelineContributor<>))
                .Select(contract => new Binding(type, contract.GetGenericArguments()[0])))
            .GroupBy(static binding => binding.ModelType)
            .OrderBy(static group => group.Key.FullName, StringComparer.Ordinal);

        foreach (var group in bindings)
        {
            var contributors = group
                .Select(binding => services.GetRequiredService(binding.ContributorType))
                .Cast<ICanonPipelineContributor>()
                .ToArray();

            _ = ConfigureModelMethod.MakeGenericMethod(group.Key)
                .Invoke(null, [builder, contributors]);
        }
    }

    private static void ConfigureModel<TModel>(CanonRuntimeBuilder builder, ICanonPipelineContributor[] contributors)
        where TModel : CanonEntity<TModel>, new()
    {
        var typed = contributors
            .Cast<ICanonPipelineContributor<TModel>>()
            .OrderBy(static contributor => contributor.Phase)
            .ThenBy(static contributor => contributor.Order)
            .ThenBy(static contributor => contributor.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        builder.ConfigurePipeline<TModel>(pipeline =>
        {
            foreach (var contributor in typed)
            {
                pipeline.AddContributor(contributor);
            }
        });
    }

    private sealed record Binding(Type ContributorType, Type ModelType);

    private sealed record CanonContributorCatalog(IReadOnlyList<Type> ContributorTypes);
}
