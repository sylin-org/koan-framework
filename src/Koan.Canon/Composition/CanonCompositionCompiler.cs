using System.Reflection;
using Koan.Core.Hosting.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Canon;

internal static class CanonCompositionCompiler
{
    private static readonly MethodInfo ConfigureModelMethod = typeof(CanonCompositionCompiler)
        .GetMethod(nameof(ConfigureModel), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static CanonCompositionPlan Discover()
    {
        var modelTypes = KoanRegistry
            .GetDiscoveredImplementors(typeof(ICanonModel))
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(static type => IsClosedSubclass(type, typeof(CanonEntity<>)))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        return Compile(modelTypes, DiscoverContributorTypes());
    }

    internal static CanonCompositionPlan Compile(
        IEnumerable<Type> modelTypes,
        IEnumerable<Type> contributorTypes)
    {
        ArgumentNullException.ThrowIfNull(modelTypes);
        ArgumentNullException.ThrowIfNull(contributorTypes);

        var models = modelTypes
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(static type => IsClosedSubclass(type, typeof(CanonEntity<>)))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        var modelSet = models.ToHashSet();
        var bindings = contributorTypes
            .SelectMany(static type => type.GetInterfaces()
                .Where(static contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(ICanonPipelineContributor<>))
                .Select(contract => new Binding(type, contract.GetGenericArguments()[0])))
            .OrderBy(static binding => binding.ContributorType.FullName, StringComparer.Ordinal)
            .ToArray();

        var invalid = bindings.FirstOrDefault(binding => !modelSet.Contains(binding.ModelType));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Canon contributor '{invalid.ContributorType.FullName}' targets undiscovered model " +
                $"'{invalid.ModelType.FullName}'. Derive the model from CanonEntity<T> and keep it discoverable.");
        }

        var byModel = bindings
            .GroupBy(static binding => binding.ModelType)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Type>)group
                    .Select(static binding => binding.ContributorType)
                    .Distinct()
                    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                    .ToArray());

        var plans = models
            .Select(type => new CanonModelPlan(
                type,
                byModel.TryGetValue(type, out var contributors) ? contributors : Array.Empty<Type>()))
            .ToArray();

        return new CanonCompositionPlan(plans);
    }

    public static IReadOnlyList<Type> DiscoverContributorTypes() => KoanRegistry
        .GetDiscoveredImplementors(typeof(ICanonPipelineContributor))
        .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
        .Distinct()
        .OrderBy(static type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    public static void Configure(CanonRuntimeBuilder builder, IServiceProvider services, CanonCompositionPlan plan)
    {
        foreach (var model in plan.Models)
        {
            var contributors = model.ContributorTypes
                .Select(services.GetRequiredService)
                .Cast<ICanonPipelineContributor>()
                .ToArray();

            _ = ConfigureModelMethod.MakeGenericMethod(model.ModelType)
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

    private static bool IsClosedSubclass(Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Binding(Type ContributorType, Type ModelType);
}
