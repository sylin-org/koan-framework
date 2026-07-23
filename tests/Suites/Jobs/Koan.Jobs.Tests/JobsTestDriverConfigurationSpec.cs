using Koan.Jobs.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Jobs.Tests;

public sealed class JobsTestDriverConfigurationSpec
{
    [Fact]
    public void Driver_rejects_a_running_background_worker_with_the_exact_correction()
    {
        var registrations = new ServiceCollection();
        registrations.AddOptions();
        registrations.Configure<JobsOptions>(options => options.EnableWorker = true);
        using var services = registrations.BuildServiceProvider();

        var act = () => JobsTestDriver.From(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JobsOptions.EnableWorker=false*");
    }

    [Fact]
    public void Driver_rejects_inline_mode_with_the_exact_correction()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<JobsOptions>(options =>
        {
            options.EnableWorker = false;
            options.Mode = JobMode.Inline;
        });
        using var provider = services.BuildServiceProvider();

        var act = () => JobsTestDriver.From(provider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JobsOptions.Mode=JobMode.Normal*");
    }
}
