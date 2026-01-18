using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Koan.Web.Json.Strict.Options;

namespace Koan.Web.Json.Strict;

public static class KoanMinimalJson
{
    public static JsonSerializerOptions CreateStrictOptions(
        KoanMinimalJsonOptions? settings = null,
        IJsonTypeInfoResolver? resolver = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        settings = Normalize(settings);
        ApplyStrict(options, settings, resolver);
        return options;
    }

    internal static void ApplyStrict(
        JsonSerializerOptions options,
        KoanMinimalJsonOptions settings,
        IJsonTypeInfoResolver? resolversFromServices)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Strict)
        {
            return;
        }

        options.AllowDuplicateProperties = settings.AllowDuplicateProperties;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;
        options.AllowTrailingCommas = false;

        var resolver = Resolve(settings, resolversFromServices);
        if (resolver is not null)
        {
            options.TypeInfoResolver = options.TypeInfoResolver is null
                ? resolver
                : JsonTypeInfoResolver.Combine(options.TypeInfoResolver, resolver);
        }
    }

    private static IJsonTypeInfoResolver? Resolve(
        KoanMinimalJsonOptions settings,
        IJsonTypeInfoResolver? servicesResolver)
    {
        if (settings.TypeInfoResolver is null && servicesResolver is null)
        {
            return null;
        }

        if (settings.TypeInfoResolver is not null && servicesResolver is not null)
        {
            return JsonTypeInfoResolver.Combine(settings.TypeInfoResolver, servicesResolver);
        }

        return settings.TypeInfoResolver ?? servicesResolver;
    }

    private static KoanMinimalJsonOptions Normalize(KoanMinimalJsonOptions? settings)
    {
        if (settings is null)
        {
            return new KoanMinimalJsonOptions
            {
                Strict = true,
                AllowDuplicateProperties = false,
                CombineRegisteredResolvers = true
            };
        }

        if (!settings.Strict)
        {
            settings.Strict = true;
        }

        return settings;
    }
}
