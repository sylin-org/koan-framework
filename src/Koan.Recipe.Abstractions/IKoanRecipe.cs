using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Recipe.Abstractions;

public interface IKoanRecipe
{
    string Name { get; }
    int Order => 0;
    bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true;
    void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env);
}