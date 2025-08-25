using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Sora.Core.Tests;

public class OptionsExtensionsTests
{
    private sealed class SampleOptions
    {
        [Required]
        public string? Name { get; set; }
        public int Threshold { get; set; } = 5;
    }

    [Fact]
    public void AddSoraOptions_BindFromSection_And_ValidateOnStart()
    {
        // Arrange
        var cfgDict = new Dictionary<string, string?>
        {
            ["Sora:Test:Name"] = "demo",
            ["Sora:Test:Threshold"] = "42"
        }!;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        var services = new ServiceCollection();

        // Act
        services.AddSoraOptions<SampleOptions>("Sora:Test");
        services.AddSingleton<IConfiguration>(cfg);
        using var sp = services.BuildServiceProvider();

        // Assert
        var opts = sp.GetRequiredService<IOptions<SampleOptions>>().Value;
        opts.Name.Should().Be("demo");
        opts.Threshold.Should().Be(42);
    }

    [Fact]
    public void AddSoraOptions_ValidateOnStart_Fails_When_Required_Missing()
    {
    var services = new ServiceCollection();
    var cfg = new ConfigurationBuilder().Build();
    services.AddSingleton<IConfiguration>(cfg);
    services.AddSoraOptions<SampleOptions>("Sora:Missing");
    using var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<IOptions<SampleOptions>>().Value.Name?.ToString();
        act.Should().Throw<OptionsValidationException>();
    }
}
