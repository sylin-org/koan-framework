using AwesomeAssertions;
using Koan.Classification.Crypto;
using Koan.Classification.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Classification.Tests;

public sealed class ClassificationModulePostureSpec
{
    [Fact]
    public async Task Production_refuses_the_development_only_ephemeral_provider()
    {
        using var provider = Services(Environments.Production).BuildServiceProvider();

        var start = () => new ClassificationModule().Start(provider, CancellationToken.None);

        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*refuses ephemeral keys*Production*IClassificationKeyProvider*");
    }

    [Fact]
    public async Task Production_accepts_an_application_supplied_key_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(Environments.Production));
        services.AddSingleton<IClassificationKeyProvider, DurableProvider>();
        new ClassificationModule().Register(services);
        using var provider = services.BuildServiceProvider();

        await new ClassificationModule().Start(provider, CancellationToken.None);

        provider.GetRequiredService<IClassificationKeyProvider>().Should().BeOfType<DurableProvider>();
    }

    private static ServiceCollection Services(string environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        new ClassificationModule().Register(services);
        return services;
    }

    private sealed class DurableProvider : IClassificationKeyProvider
    {
        private readonly ClassificationDataKey _key = new("durable", new byte[32]);
        public ClassificationDataKey GetActiveKey(string scope) => _key;
        public ClassificationDataKey GetForDecrypt(string keyId) => _key;
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Classification.Spec";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
