using AwesomeAssertions;
using Koan.ZenGarden.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        using var services = BuildServices(_ => { });

        var resolve = () => services.GetRequiredService<IOptions<ZenGardenOptions>>().Value;

        resolve.Should().NotThrow();
    }

    [Fact]
    public void Non_positive_http_timeout_identifies_the_invalid_setting()
    {
        using var services = BuildServices(options => options.HttpTimeoutSeconds = 0);

        var resolve = () => services.GetRequiredService<IOptions<ZenGardenOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(failure => failure.Contains(nameof(ZenGardenOptions.HttpTimeoutSeconds)));
    }

    [Fact]
    public void Non_positive_koi_duration_identifies_the_invalid_setting()
    {
        using var services = BuildServices(options => options.KoiRetryInterval = TimeSpan.Zero);

        var resolve = () => services.GetRequiredService<IOptions<ZenGardenOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(
                $"Koan:ZenGarden:{nameof(ZenGardenOptions.KoiRetryInterval)} must be greater than zero.");
    }

    private static ServiceProvider BuildServices(Action<ZenGardenOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddZenGardenRuntime(configure: configure);
        return services.BuildServiceProvider();
    }
}
