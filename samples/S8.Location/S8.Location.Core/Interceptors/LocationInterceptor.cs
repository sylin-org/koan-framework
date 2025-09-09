using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S8.Location.Core.Models;
using S8.Location.Core.Services;
using Sora.Flow.Core.Interceptors;
using Sora.Core.Hosting.Bootstrap;
using Sora.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace S8.Location.Core.Interceptors;

/// <summary>
/// Auto-registrar that configures Location intake interceptors for hash collision detection
/// and native Flow parking for records awaiting resolution.
/// </summary>
public class LocationInterceptor : ISoraAutoRegistrar
{
    public string ModuleName => "S8.Location.Core.Interceptors";
    public string? ModuleVersion => GetType().Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Console.WriteLine("[LocationInterceptor] Registering hash collision detection interceptor");
        
        // Register the hash-collision interceptor using Sora.Flow native parking
        FlowIntakeInterceptors.RegisterForType<Models.Location>(location =>
        {
            // Basic validation - park for background resolution if valid
            if (string.IsNullOrWhiteSpace(location.Address))
            {
                return FlowIntakeActions.Drop(location);
            }
            
            // Park all new locations for background hash collision detection and resolution
            return FlowIntakeActions.Park(location, "WAITING_ADDRESS_RESOLVE");
        });
        
        Console.WriteLine("[LocationInterceptor] Hash collision detection interceptor registered successfully");
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("LocationIntakeInterceptor", "Registered");
    }
}