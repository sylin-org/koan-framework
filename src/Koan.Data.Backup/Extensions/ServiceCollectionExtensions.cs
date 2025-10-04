using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Core;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Services;
using Koan.Data.Backup.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Data.Backup.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Koan backup and restore services to the DI container
    /// </summary>
    public static IServiceCollection AddKoanBackupRestore(this IServiceCollection services)
    {
        return services.AddKoanBackupRestoreCore(null);
    }

    /// <summary>
    /// Adds Koan backup and restore services to the DI container with options configuration
    /// </summary>
    public static IServiceCollection AddKoanBackupRestore(this IServiceCollection services, Action<BackupRestoreOptions> configureOptions)
    {
        return services.AddKoanBackupRestoreCore(configureOptions);
    }

    /// <summary>
    /// Core implementation for adding Koan backup and restore services
    /// </summary>
    private static IServiceCollection AddKoanBackupRestoreCore(
        this IServiceCollection services,
        Action<BackupRestoreOptions>? configureOptions)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Core services
        services.TryAddScoped<IBackupService, StreamingBackupService>();
        services.TryAddScoped<IRestoreService, OptimizedRestoreService>();
        services.TryAddScoped<IEntityDiscoveryService, EntityDiscoveryService>();
        services.TryAddScoped<IBackupDiscoveryService, BackupDiscoveryService>();

        // Storage services
        services.TryAddScoped<BackupStorageService>();

        // Background services (optional)
        services.TryAddSingleton<IHostedService, BackupMaintenanceService>();

        return services;
    }

    /// <summary>
    /// Adds Koan backup and restore services with custom implementations
    /// </summary>
    public static IServiceCollection AddKoanBackupRestore<TBackupService, TRestoreService>(
        this IServiceCollection services,
        Action<BackupRestoreOptions>? configureOptions = null)
        where TBackupService : class, IBackupService
        where TRestoreService : class, IRestoreService
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Custom service implementations
        services.TryAddScoped<IBackupService, TBackupService>();
        services.TryAddScoped<IRestoreService, TRestoreService>();

        // Standard services
        services.TryAddScoped<IEntityDiscoveryService, EntityDiscoveryService>();
        services.TryAddScoped<IBackupDiscoveryService, BackupDiscoveryService>();
        services.TryAddScoped<BackupStorageService>();

        return services;
    }

    /// <summary>
    /// Adds only the backup services (for read-only scenarios)
    /// </summary>
    public static IServiceCollection AddKoanBackupOnly(this IServiceCollection services)
    {
        services.TryAddScoped<IBackupService, StreamingBackupService>();
        services.TryAddScoped<IEntityDiscoveryService, EntityDiscoveryService>();
        services.TryAddScoped<IBackupDiscoveryService, BackupDiscoveryService>();
        services.TryAddScoped<BackupStorageService>();

        return services;
    }

    /// <summary>
    /// Adds only the restore services (for restore-only scenarios)
    /// </summary>
    public static IServiceCollection AddKoanRestoreOnly(this IServiceCollection services)
    {
        services.TryAddScoped<IRestoreService, OptimizedRestoreService>();
        services.TryAddScoped<IBackupDiscoveryService, BackupDiscoveryService>();
        services.TryAddScoped<BackupStorageService>();

        return services;
    }

    /// <summary>
    /// Adds entity discovery services for backup/restore integrations
    /// </summary>
    public static IServiceCollection AddKoanEntityDiscovery(this IServiceCollection services)
    {
        services.TryAddScoped<IEntityDiscoveryService, EntityDiscoveryService>();
        return services;
    }

    /// <summary>
    /// Adds backup inventory health check to monitor entity backup coverage.
    /// </summary>
    /// <remarks>
    /// Reports healthy when all entities have backup coverage.
    /// Reports degraded when entities lack backup attributes.
    /// Use with services.AddHealthChecks().AddCheck&lt;BackupInventoryHealthCheck&gt;("backup-inventory").
    /// </remarks>
    public static IServiceCollection AddBackupInventoryHealthCheck(this IServiceCollection services)
    {
        services.TryAddScoped<Services.BackupInventoryHealthCheck>();
        return services;
    }

    private static IServiceCollection TryAddHostedService<TService>(this IServiceCollection services)
        where TService : class, Microsoft.Extensions.Hosting.IHostedService
    {
        services.TryAddSingleton<Microsoft.Extensions.Hosting.IHostedService, TService>();
        return services;
    }
}