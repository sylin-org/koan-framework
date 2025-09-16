using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S8.Location.Core.Models;
using S8.Location.Core.Services;
using Koan.Flow.Core.Interceptors;
using Koan.Flow.Core.Infrastructure;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core;
using Koan.Data.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace S8.Location.Core.Interceptors;

/// <summary>
/// Auto-registrar that configures Location intake interceptors using the new fluent lifecycle API
/// for proper hash collision detection and intelligent parking decisions.
/// </summary>
public class LocationInterceptor : IKoanAutoRegistrar
{
    public string ModuleName => "S8.Location.Core.Interceptors";
    public string? ModuleVersion => GetType().Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Console.WriteLine("[LocationInterceptor] Registering fluent lifecycle interceptors");
        
        // Register using the new fluent lifecycle API with proper hash collision detection
        FlowInterceptors
            .For<Models.Location>()
            .BeforeIntake(async location =>
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(location.Address))
                {
                    Console.WriteLine("[LocationInterceptor] Dropping location with empty address");
                    return FlowIntakeActions.Drop(location, "Empty address field");
                }

                // Compute hash for collision detection (core requirement from CLD document)
                var addressService = services.BuildServiceProvider().GetService<IAddressResolutionService>();
                if (addressService != null)
                {
                    var normalized = addressService.NormalizeAddress(location.Address);
                    var hash = addressService.ComputeSHA512(normalized);
                    location.AddressHash = hash;
                    
                    // Check for hash collision (duplicate address already processed)
                    var existingCache = await Data<ResolutionCache, string>.GetAsync(hash);
                    if (existingCache != null)
                    {
                        Console.WriteLine("[LocationInterceptor] Hash collision detected - dropping duplicate address");
                        return FlowIntakeActions.Drop(location, $"Duplicate address hash: {hash}");
                    }
                    
                    Console.WriteLine("[LocationInterceptor] New address hash {0} - parking for resolution", hash.Substring(0, 8));
                }

                // New unique address - park for background resolution
                return FlowIntakeActions.Park(location, "WAITING_ADDRESS_RESOLVE");
            })
            .AfterAssociation(async location =>
            {
                // Post-association processing - notify external systems
                if (!string.IsNullOrEmpty(location.AgnosticLocationId))
                {
                    Console.WriteLine("[LocationInterceptor] Location successfully associated with canonical ID: {0}", 
                        location.AgnosticLocationId);
                    
                    // Could trigger external system notifications here
                    // await _notificationService.NotifyLocationResolved(location);
                }
                
                return FlowStageActions.Continue(location);
            })
            .BeforeProjection(async location =>
            {
                // Enrich location before canonical projection
                Console.WriteLine("[LocationInterceptor] Enriching location {0} before projection", 
                    location.Id ?? "unknown");
                
                return FlowStageActions.Continue(location);
            });
        
        Console.WriteLine("[LocationInterceptor] Fluent lifecycle interceptors registered successfully");
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddSetting("LocationIntakeInterceptor", "Registered");
    }
}