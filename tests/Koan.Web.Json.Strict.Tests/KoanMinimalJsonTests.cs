using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Koan.Web.Json.Strict;
using Koan.Web.Json.Strict.Infrastructure;
using MinimalJsonOptions = Koan.Web.Json.Strict.Options.KoanMinimalJsonOptions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Json.Strict.Tests;

public sealed class KoanMinimalJsonTests
{
    [Fact]
    public void CreateStrictOptions_DisallowsDuplicates()
    {
        var options = KoanMinimalJson.CreateStrictOptions();

        options.AllowDuplicateProperties.Should().BeFalse();
        options.AllowTrailingCommas.Should().BeFalse();
    }

    [Fact]
    public void Configure_StrictDisabled_NoMutation()
    {
        var settings = new MinimalJsonOptions
        {
            Strict = false,
            AllowDuplicateProperties = true
        };

        var configurator = new KoanMinimalJsonOptionsConfigurator(
            global::Microsoft.Extensions.Options.Options.Create(settings),
            []);

        var jsonOptions = new JsonOptions();
        jsonOptions.SerializerOptions.AllowDuplicateProperties = true;

        configurator.Configure(jsonOptions);

        jsonOptions.SerializerOptions.AllowDuplicateProperties.Should().BeTrue();
    }

    [Fact]
    public void Configure_StrictEnabled_AppliesResolverChain()
    {
        var resolver = new TrackingResolver();
        var settings = new MinimalJsonOptions
        {
            Strict = true,
            AllowDuplicateProperties = false,
            TypeInfoResolver = resolver,
            CombineRegisteredResolvers = false
        };

        var configurator = new KoanMinimalJsonOptionsConfigurator(
            global::Microsoft.Extensions.Options.Options.Create(settings),
            []);

        var jsonOptions = new JsonOptions();
        jsonOptions.SerializerOptions.AllowDuplicateProperties = true;

        configurator.Configure(jsonOptions);

        jsonOptions.SerializerOptions.AllowDuplicateProperties.Should().BeFalse();
        jsonOptions.SerializerOptions.TypeInfoResolver.Should().NotBeNull();
        jsonOptions.SerializerOptions.TypeInfoResolver!.ToString().Should().Contain(nameof(TrackingResolver));
    }

    private sealed class TrackingResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != typeof(TestPayload))
            {
                return null;
            }
            return JsonTypeInfo.CreateJsonTypeInfo<TestPayload>(options);
        }
    }

    private sealed class TestPayload
    {
        public string? Name { get; set; }
    }
}
