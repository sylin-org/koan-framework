using System.Text.Json.Serialization.Metadata;
using Koan.Web.Json.Strict.Options;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Koan.Web.Json.Strict.Infrastructure;

internal sealed class KoanMinimalJsonOptionsConfigurator : IConfigureOptions<JsonOptions>
{
    private readonly IOptions<KoanMinimalJsonOptions> _options;
    private readonly IEnumerable<IJsonTypeInfoResolver> _resolvers;

    public KoanMinimalJsonOptionsConfigurator(
        IOptions<KoanMinimalJsonOptions> options,
        IEnumerable<IJsonTypeInfoResolver> resolvers)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _resolvers = resolvers ?? Array.Empty<IJsonTypeInfoResolver>();
    }

    public void Configure(JsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = _options.Value;
        if (!settings.Strict)
        {
            return;
        }

        var resolved = settings.CombineRegisteredResolvers
            ? CombineResolvers(_resolvers)
            : null;

        KoanMinimalJson.ApplyStrict(options.SerializerOptions, settings, resolved);
    }

    private static IJsonTypeInfoResolver? CombineResolvers(IEnumerable<IJsonTypeInfoResolver> resolvers)
    {
        var list = resolvers as IJsonTypeInfoResolver[] ?? resolvers.ToArray();
        if (list.Length == 0)
        {
            return null;
        }

        return list.Length == 1 ? list[0] : JsonTypeInfoResolver.Combine(list);
    }
}
