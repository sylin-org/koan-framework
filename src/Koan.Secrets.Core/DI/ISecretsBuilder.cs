using Koan.Secrets.Abstractions;

namespace Koan.Secrets.Core.DI;

public interface ISecretsBuilder
{
    ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider;
}