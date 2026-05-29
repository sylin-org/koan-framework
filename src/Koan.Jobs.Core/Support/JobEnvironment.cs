using System;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Jobs.Options;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Support;

internal static class JobEnvironment
{
    private static IServiceProvider ServiceProvider
        => AppHost.Current ?? throw new InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() has been executed and the host is running.");

    internal static JobsOptions Options => ServiceProvider.GetRequiredService<IOptions<JobsOptions>>().Value;

    internal static JobProgressBroker ProgressBroker => ServiceProvider.GetRequiredService<JobProgressBroker>();
}
