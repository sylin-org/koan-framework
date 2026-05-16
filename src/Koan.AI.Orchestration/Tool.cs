namespace Koan.AI.Orchestration;

/// <summary>
/// A tool that can be used by chains and agents for function calling.
/// Tools are created from DI services or entity operations.
///
/// <code>
/// Chain.Create()
///     .WithTools(
///         Tool.From&lt;IWeatherService&gt;("GetForecast", "GetAlerts"),
///         Tool.FromEntity&lt;Todo&gt;("Query", "Save"))
///     .Chat("...")
///     .Run();
/// </code>
/// </summary>
public sealed class Tool
{
    /// <summary>Tool name.</summary>
    public string Name { get; }

    /// <summary>Source type (service or entity type).</summary>
    public Type SourceType { get; }

    /// <summary>Exposed method names.</summary>
    public IReadOnlyList<string> Methods { get; }

    /// <summary>Whether this tool allows write operations.</summary>
    public bool AllowWrite { get; }

    private Tool(string name, Type sourceType, IReadOnlyList<string> methods, bool allowWrite)
    {
        Name = name;
        SourceType = sourceType;
        Methods = methods;
        AllowWrite = allowWrite;
    }

    /// <summary>
    /// Create tools from a DI service, exposing the specified methods as callable functions.
    /// </summary>
    public static Tool From<TService>(params string[] methods) =>
        new(typeof(TService).Name, typeof(TService), methods, allowWrite: false);

    /// <summary>
    /// Create tools from an entity type, exposing entity operations (Query, Get, Save, Delete).
    /// </summary>
    public static Tool FromEntity<T>(params string[] operations) =>
        new(typeof(T).Name, typeof(T), operations, allowWrite: operations.Length > 0);
}
