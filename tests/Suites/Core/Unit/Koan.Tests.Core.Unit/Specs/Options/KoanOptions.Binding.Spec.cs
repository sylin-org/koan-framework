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
