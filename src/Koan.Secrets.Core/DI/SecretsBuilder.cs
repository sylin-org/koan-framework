using Microsoft.Extensions.DependencyInjection;
using Koan.Secrets.Abstractions;

namespace Koan.Secrets.Core.DI;

internal sealed class SecretsBuilder(IServiceCollection services) : ISecretsBuilder
{
    public ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider
    {
        services.AddSingleton<ISecretProvider, T>();
        return this;
    }
}