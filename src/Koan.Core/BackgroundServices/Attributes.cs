using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.BackgroundServices;

/// <summary>
/// Configures a background service via attributes
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class KoanBackgroundServiceAttribute : Attribute
{
    public bool Enabled { get; set; } = true;
    public string? ConfigurationSection { get; set; }
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Singleton;
    public int Priority { get; set; } = 100;
    public bool RunInDevelopment { get; set; } = true;
    public bool RunInProduction { get; set; } = true;
    public bool RunInTesting { get; set; } = false;
}

/// <summary>
/// Configures a periodic service via attributes
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class KoanPeriodicServiceAttribute : KoanBackgroundServiceAttribute
{
    public int IntervalSeconds { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 0;
    public bool RunOnStartup { get; set; } = false;
}

/// <summary>
/// Configures a startup service via attributes
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class KoanStartupServiceAttribute : KoanBackgroundServiceAttribute
{
    public int StartupOrder { get; set; } = 100;
    public bool ContinueOnFailure { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Declares a service action that can be called via the fluent API
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ServiceActionAttribute : Attribute
{
    public string Name { get; }
    public string? Description { get; set; }
    public bool RequiresParameters { get; set; }
    public Type? ParametersType { get; set; }

    public ServiceActionAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Declares a service event that can be emitted and subscribed to
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class ServiceEventAttribute : Attribute
{
    public string Name { get; }
    public string? Description { get; set; }
    public Type? EventArgsType { get; set; }

    public ServiceEventAttribute(string name)
    {
        Name = name;
    }
}