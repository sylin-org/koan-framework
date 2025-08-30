using Sora.Secrets.Abstractions;

namespace Sora.Secrets.Core.DI;

public interface ISecretsBuilder
{
    ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider;
}