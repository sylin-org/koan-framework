using Microsoft.Extensions.DependencyInjection;
using Sora.Secrets.Abstractions;

namespace Sora.Secrets.Core.DI;

internal sealed class SecretsBuilder(IServiceCollection services) : ISecretsBuilder
{
    public ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider
    {
        services.AddSingleton<ISecretProvider, T>();
        return this;
    }
}