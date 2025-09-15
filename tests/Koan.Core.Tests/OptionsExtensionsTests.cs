using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Core.Modules;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Koan.Core.Tests;

public class OptionsExtensionsTests
{
    private sealed class SampleOptions
    {
        [Required]
        public string? Name { get; set; }
        public int Threshold { get; set; } = 5;
    }

    [Fact]
    public void AddKoanOptions_BindFromSection_And_ValidateOnStart()
    {
        // Arrange
        var cfgDict = new Dictionary<string, string?>
        {
            ["Koan:Test:Name"] = "demo",
            ["Koan:Test:Threshold"] = "42"
        }!;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        var services = new ServiceCollection();

        // Act
        services.AddKoanOptions<SampleOptions>("Koan:Test");
        services.AddSingleton<IConfiguration>(cfg);
        using var sp = services.BuildServiceProvider();

        // Assert
        var opts = sp.GetRequiredService<IOptions<SampleOptions>>().Value;
        opts.Name.Should().Be("demo");
        opts.Threshold.Should().Be(42);
    }

    [Fact]
    public void AddKoanOptions_ValidateOnStart_Fails_When_Required_Missing()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddKoanOptions<SampleOptions>("Koan:Missing");
        using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<IOptions<SampleOptions>>().Value.Name?.ToString();
        act.Should().Throw<OptionsValidationException>();
    }
}
