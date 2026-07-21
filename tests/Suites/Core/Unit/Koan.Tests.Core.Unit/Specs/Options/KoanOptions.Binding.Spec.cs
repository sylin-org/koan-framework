using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Koan.Tests.Core.Unit.Specs.Options;

public sealed class KoanOptionsBindingSpec
{
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

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.AddSingleton<IHostApplicationLifetime, NoopHostApplicationLifetime>();
        return services;
    }

    [Fact]
    public async Task Binds_configuration_section_and_validates_on_start()
    {
        var services = CreateServices();

        var cfgDict = new Dictionary<string, string?>
        {
            ["Koan:Test:Name"] = "demo",
            ["Koan:Test:Threshold"] = "42"
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoanOptions<SampleOptions>("Koan:Test");

        await using var sp = services.BuildServiceProvider();

        var opts = sp
            .GetRequiredService<IOptions<SampleOptions>>().Value;
        opts.Name.Should().Be("demo");
        opts.Threshold.Should().Be(42);
    }

    [Fact]
    public async Task Uses_defaults_when_configuration_is_missing()
    {
        var services = CreateServices();

        services.AddKoanOptions<OptionalOptions>("Koan:Missing");

        await using var sp = services.BuildServiceProvider();

        var opts = sp
            .GetRequiredService<IOptions<OptionalOptions>>().Value;
        opts.Name.Should().BeNull();
        opts.Threshold.Should().Be(11);
    }

    [Fact]
    public async Task Skips_validate_on_start_when_disabled_but_validates_on_access()
    {
        var services = CreateServices();

        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoanOptions<SampleOptions>("Koan:Missing", validateOnStart: false);

        await using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<SampleOptions>>();

        Func<object?> act = () => options.Value;
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task Honors_layering_conventions_for_defaults_configuration_and_overrides()
    {
        var services = CreateServices();

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

        await using var sp = services.BuildServiceProvider();

        var opts = sp
            .GetRequiredService<IOptions<SampleOptions>>().Value;
        opts.Name.Should().Be("forced");
        opts.Threshold.Should().Be(13);
    }

    [Fact]
    public async Task Throws_when_required_value_missing()
    {
        var services = CreateServices();

        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKoanOptions<SampleOptions>("Koan:Missing");

        await using var sp = services.BuildServiceProvider();

        Action act = () => sp.GetRequiredService<IOptions<SampleOptions>>().Value.Name?.ToString();
        act.Should().Throw<OptionsValidationException>();
    }
}
