using Microsoft.Extensions.DependencyInjection;

namespace Sora.Core;

public interface ISoraInitializer
{
    void Initialize(IServiceCollection services);
}