using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core;

public interface IKoanInitializer
{
    void Initialize(IServiceCollection services);
}