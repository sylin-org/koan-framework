using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Recipe.Abstractions;

internal sealed class HostingEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "KoanApp";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}