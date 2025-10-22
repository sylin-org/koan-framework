using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Xunit.Abstractions;

namespace Koan.Tests.Core.Unit.Specs.Options;

public sealed class KoanOptionsBindingSpec
{
    private readonly ITestOutputHelper _output;

    public KoanOptionsBindingSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class SampleOptions
    {
        [Required]
        public string? Name { get; set; }

        public int Threshold { get; set; } = 5;
    }

    private sealed class OptionalOptions
    {
        public string? Name { get; set; }

        public int Threshold { get; set; } = 11;
    }

    [Fact]
    public async Task Binds_configuration_section_and_validates_on_start()
    {
        await TestPipeline.For<KoanOptionsBindingSpec>(_output, nameof(Binds_configuration_section_and_validates_on_start))
            .UsingServiceProvider(configure: (ctx, services) =>
            {
                var cfgDict = new Dictionary<string, string?>
                {
                    ["Koan:Test:Name"] = "demo",
                    ["Koan:Test:Threshold"] = "42"
                };

                var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddKoanOptions<SampleOptions>("Koan:Test");
            })
            .Assert(ctx =>
            {
                var opts = ctx.GetServiceProvider()
                    .GetRequiredService<IOptions<SampleOptions>>().Value;
                opts.Name.Should().Be("demo");
                opts.Threshold.Should().Be(42);
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Uses_defaults_when_configuration_is_missing()
    {
        await TestPipeline.For<KoanOptionsBindingSpec>(_output, nameof(Uses_defaults_when_configuration_is_missing))
            .UsingServiceProvider(configure: (ctx, services) =>
            {
                services.AddKoanOptions<OptionalOptions>("Koan:Missing");
            })
            .Assert(ctx =>
            {
                var opts = ctx.GetServiceProvider()
                    .GetRequiredService<IOptions<OptionalOptions>>().Value;
                opts.Name.Should().BeNull();
                opts.Threshold.Should().Be(11);
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Skips_validate_on_start_when_disabled_but_validates_on_access()
    {
        await TestPipeline.For<KoanOptionsBindingSpec>(_output, nameof(Skips_validate_on_start_when_disabled_but_validates_on_access))
            .UsingServiceProvider(configure: (ctx, services) =>
            {
                var configuration = new ConfigurationBuilder().Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddKoanOptions<SampleOptions>("Koan:Missing", validateOnStart: false);
            })
            .Assert(ctx =>
            {
                var provider = ctx.GetServiceProvider();
                var options = provider.GetRequiredService<IOptions<SampleOptions>>();

                Func<object?> act = () => options.Value;
                act.Should().Throw<OptionsValidationException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Honors_layering_conventions_for_defaults_configuration_and_overrides()
    {
        await TestPipeline.For<KoanOptionsBindingSpec>(_output, nameof(Honors_layering_conventions_for_defaults_configuration_and_overrides))
            .UsingServiceProvider(configure: (ctx, services) =>
            {
                var cfgDict = new Dictionary<string, string?>
                {
                    ["Koan:Layered:Name"] = "cfg",
                    ["Koan:Layered:Threshold"] = "29"
                };

                var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
                services.AddSingleton<IConfiguration>(configuration);

                services
                    .AddKoanOptions<SampleOptions>()
                    .WithProviderDefaults(opts =>
                    {
                        opts.Name = "provider";
                        opts.Threshold = 3;
                    })
                    .WithRecipeDefaults(opts =>
                    {
                        opts.Name = "recipe";
                        opts.Threshold = 7;
                    })
                    .BindFromConfiguration(configuration.GetSection("Koan:Layered"))
                    .WithCodeOverrides(opts => opts.Threshold = 13)
                    .WithRecipeForcedOverrides(opts => opts.Name = "forced");
            })
            .Assert(ctx =>
            {
                var opts = ctx.GetServiceProvider()
                    .GetRequiredService<IOptions<SampleOptions>>().Value;
                opts.Name.Should().Be("forced");
                opts.Threshold.Should().Be(13);
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }

    [Fact]
    public async Task Throws_when_required_value_missing()
    {
        await TestPipeline.For<KoanOptionsBindingSpec>(_output, nameof(Throws_when_required_value_missing))
            .UsingServiceProvider(configure: (ctx, services) =>
            {
                var configuration = new ConfigurationBuilder().Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddKoanOptions<SampleOptions>("Koan:Missing");
            })
            .Assert(ctx =>
            {
                var provider = ctx.GetServiceProvider();
                Action act = () => provider.GetRequiredService<IOptions<SampleOptions>>().Value.Name?.ToString();
                act.Should().Throw<OptionsValidationException>();
                return ValueTask.CompletedTask;
            })
            .RunAsync();
    }
}
