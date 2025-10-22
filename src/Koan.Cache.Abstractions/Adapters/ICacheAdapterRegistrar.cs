using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Cache.Abstractions.Adapters;

public interface ICacheAdapterRegistrar
{
    string Name { get; }

    void Register(IServiceCollection services, IConfiguration configuration);
}
