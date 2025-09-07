using System;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sora.Flow.Model;
using Sora.Flow.Sending;
using Sora.Messaging;

namespace Sora.Flow.Configuration;

/// <summary>
/// Service collection extensions for configuring Flow message handlers properly.
/// </summary>
public static class FlowServiceExtensions
{
    /// <summary>
    /// Configure Flow message handlers during service registration.
    /// Usage: <c>services.ConfigureFlow(flow => flow.On&lt;Device&gt;(device => ProcessDevice(device)))</c>
    /// </summary>
    public static IServiceCollection ConfigureFlow(this IServiceCollection services, Action<FlowHandlerConfigurator> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        
        var configurator = new FlowHandlerConfigurator(services);
        configure(configurator);
        
        return services;
    }

    /// <summary>
    /// Automatically configure Flow handlers for all FlowEntity and FlowValueObject types with automatic Flow intake routing.
    /// This eliminates boilerplate by registering standard handlers that log the entity/value object and route to Flow intake.
    /// Optional: Specify assemblies to scan, otherwise scans the calling assembly.
    /// 
    /// Usage: 
    /// - services.AutoConfigureFlow() // Scans calling assembly
    /// - services.AutoConfigureFlow(typeof(Device).Assembly, typeof(Reading).Assembly) // Scans specific assemblies
    /// 
    /// For hybrid usage with custom handlers:
    /// services.AutoConfigureFlow(typeof(Device).Assembly)
    ///    .ConfigureFlow(flow => flow.On&lt;SpecialEntity&gt;(entity => CustomHandling(entity)));
    /// 
    /// Note: Custom handlers registered with ConfigureFlow() will override auto-registered handlers for the same type.
    /// </summary>
    public static IServiceCollection AutoConfigureFlow(this IServiceCollection services, params Assembly[] assemblies)
    {
        assemblies = assemblies.Length > 0 ? assemblies : new[] { Assembly.GetCallingAssembly() };
        
        var configurator = new FlowHandlerConfigurator(services);
        configurator.AutoRegisterHandlers(assemblies);
        
        return services;
    }

    /// <summary>
    /// Automatically configure Flow handlers with custom logging configuration.
    /// Allows customization of log messages while maintaining automatic Flow intake routing.
    /// </summary>
    public static IServiceCollection AutoConfigureFlow(this IServiceCollection services, 
        Action<AutoFlowOptions> configure, 
        params Assembly[] assemblies)
    {
        assemblies = assemblies.Length > 0 ? assemblies : new[] { Assembly.GetCallingAssembly() };
        
        var options = new AutoFlowOptions();
        configure(options);
        
        var configurator = new FlowHandlerConfigurator(services);
        configurator.AutoRegisterHandlers(options, assemblies);
        
        return services;
    }
}

/// <summary>
/// Configuration options for automatic Flow handler registration.
/// </summary>
public sealed class AutoFlowOptions
{
    /// <summary>
    /// Whether to enable console logging for received entities and value objects.
    /// Default: true
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Custom format for entity log messages. Parameters: {TypeName}, {KeyInfo}
    /// Default: "[FLOW] Entity {TypeName} {KeyInfo}"
    /// </summary>
    public string EntityLogFormat { get; set; } = "[FLOW] Entity {0} {1}";

    /// <summary>
    /// Custom format for value object log messages. Parameters: {TypeName}, {KeyInfo}
    /// Default: "[FLOW] ValueObject {TypeName} {KeyInfo}"
    /// </summary>
    public string ValueObjectLogFormat { get; set; } = "[FLOW] ValueObject {0} {1}";

    /// <summary>
    /// Predicate to filter which types should have handlers auto-registered.
    /// Default: null (all FlowEntity and FlowValueObject types are registered)
    /// </summary>
    public Func<Type, bool>? TypeFilter { get; set; }
}

/// <summary>
/// Fluent configurator for Flow message handlers that integrates with DI properly.
/// </summary>
public sealed class FlowHandlerConfigurator
{
    private readonly IServiceCollection _services;

    internal FlowHandlerConfigurator(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Register a handler for typed messages.
    /// Usage: <c>flow.On&lt;Device&gt;(device => ProcessDevice(device))</c>
    /// </summary>
    public FlowHandlerConfigurator On<T>(Func<T, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowTargetedMessage<T>>(async msg =>
        {
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Register a handler for typed messages with cancellation support.
    /// </summary>
    public FlowHandlerConfigurator On<T>(Func<T, CancellationToken, Task> handler) where T : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowTargetedMessage<T>>(async msg =>
        {
            if (ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Entity, CancellationToken.None);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Register a handler for named commands.
    /// Usage: flow.On("seed", payload => HandleSeed(payload))
    /// </summary>
    public FlowHandlerConfigurator On(string command, Func<object?, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Register a handler for named commands with cancellation support.
    /// </summary>
    public FlowHandlerConfigurator On(string command, Func<object?, CancellationToken, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be null or empty", nameof(command));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        
        _services.On<FlowCommandMessage>(async msg =>
        {
            if (string.Equals(msg.Command, command, StringComparison.OrdinalIgnoreCase) &&
                ShouldProcessMessage(msg.Target))
            {
                await handler(msg.Payload, CancellationToken.None);
            }
        });
        
        return this;
    }

    /// <summary>
    /// Automatically register handlers for all FlowEntity and FlowValueObject types in the specified assemblies.
    /// Each handler logs the entity/value object details and routes it to Flow intake for processing.
    /// </summary>
    public FlowHandlerConfigurator AutoRegisterHandlers(params Assembly[] assemblies)
    {
        return AutoRegisterHandlers(null, assemblies);
    }

    /// <summary>
    /// Automatically register handlers with custom options for all FlowEntity and FlowValueObject types.
    /// </summary>
    public FlowHandlerConfigurator AutoRegisterHandlers(AutoFlowOptions? options, params Assembly[] assemblies)
    {
        if (assemblies == null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        options ??= new AutoFlowOptions();

        // Scanning assemblies for Flow types
        int totalHandlersRegistered = 0;
        
        foreach (var assembly in assemblies)
        {
            // Scanning assembly for Flow types
            var types = GetTypesFromAssembly(assembly);
            // Found types in assembly
            
            foreach (var type in types)
            {
                // Apply type filter if specified
                if (options.TypeFilter != null && !options.TypeFilter(type))
                {
                    continue;
                }

                // Check if it's a DynamicFlowEntity<T> first
                var dynamicEntityBaseType = GetDynamicFlowEntityBaseType(type);
                if (dynamicEntityBaseType != null)
                {
                    // Registering DynamicFlowEntity handler
                    RegisterDynamicFlowEntityHandler(type, dynamicEntityBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }

                // Check if it's a FlowEntity<T>
                var entityBaseType = GetFlowEntityBaseType(type);
                if (entityBaseType != null)
                {
                    // Registering FlowEntity handler
                    RegisterFlowEntityHandler(type, entityBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }

                // Check if it's a FlowValueObject<T>
                var valueObjectBaseType = GetFlowValueObjectBaseType(type);
                if (valueObjectBaseType != null)
                {
                    // Registering FlowValueObject handler
                    RegisterFlowValueObjectHandler(type, valueObjectBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }
            }
        }

        // AutoConfigureFlow registration complete
        return this;
    }

    // Semantic aliases for clarity
    public FlowHandlerConfigurator OnCommand<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnCommand<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnCommand(string command, Func<object?, Task> handler) => On(command, handler);
    public FlowHandlerConfigurator OnEvent<T>(Func<T, Task> handler) where T : class => On(handler);
    public FlowHandlerConfigurator OnEvent<T>(Func<T, CancellationToken, Task> handler) where T : class => On(handler);

    private static bool ShouldProcessMessage(string? target)
    {
        // If no target specified, it's a broadcast - process it
        if (string.IsNullOrWhiteSpace(target)) return true;
        
        // TODO: Implement adapter identity matching
        return true;
    }

    private static Type[] GetTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).ToArray()!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static Type? GetDynamicFlowEntityBaseType(Type type)
    {
        if (type.IsAbstract || type.IsInterface) return null;
        
        var current = type;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var generic = current.GetGenericTypeDefinition();
                if (generic == typeof(DynamicFlowEntity<>))
                {
                    return current;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private static Type? GetFlowEntityBaseType(Type type)
    {
        if (type.IsAbstract || type.IsInterface) return null;
        
        var current = type;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var generic = current.GetGenericTypeDefinition();
                if (generic == typeof(FlowEntity<>))
                {
                    return current;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private static Type? GetFlowValueObjectBaseType(Type type)
    {
        if (type.IsAbstract || type.IsInterface) return null;
        
        var current = type;
        while (current != null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var generic = current.GetGenericTypeDefinition();
                if (generic == typeof(FlowValueObject<>))
                {
                    return current;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private void RegisterFlowEntityHandler(Type entityType, Type baseType, AutoFlowOptions options)
    {
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            return;
        }

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(entityType);

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(entityType, typeof(Task));
        var handler = CreateEntityHandler(entityType, options);

        // Invoke On<EntityType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
        }
        catch (Exception)
        {
            // Silent failure - registration will be attempted again if needed
        }
    }

    private void RegisterDynamicFlowEntityHandler(Type entityType, Type baseType, AutoFlowOptions options)
    {
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            return;
        }

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(entityType);

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(entityType, typeof(Task));
        var handler = CreateEntityHandler(entityType, options);

        // Invoke On<EntityType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
        }
        catch (Exception)
        {
            // Silent failure - registration will be attempted again if needed
        }
    }

    private void RegisterFlowValueObjectHandler(Type valueObjectType, Type baseType, AutoFlowOptions options)
    {
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            return;
        }

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(valueObjectType);

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(valueObjectType, typeof(Task));
        var handler = CreateValueObjectHandler(valueObjectType, options);

        // Invoke On<ValueObjectType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
        }
        catch (Exception)
        {
            // Silent failure - registration will be attempted again if needed
        }
    }

    private Delegate CreateEntityHandler(Type entityType, AutoFlowOptions options)
    {
        // Create a closure that captures the options
        var handlerType = typeof(Func<,>).MakeGenericType(entityType, typeof(Task));
        
        // Check if this is a DynamicFlowEntity<T>
        if (IsDynamicFlowEntity(entityType))
        {
            var method = typeof(FlowHandlerConfigurator).GetMethod(nameof(CreateDynamicEntityHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Static);
            var genericMethod = method!.MakeGenericMethod(entityType);
            return (Delegate)genericMethod.Invoke(null, new object[] { options })!;
        }
        else
        {
            // Regular FlowEntity<T>
            var method = typeof(FlowHandlerConfigurator).GetMethod(nameof(CreateEntityHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Static);
            var genericMethod = method!.MakeGenericMethod(entityType);
            return (Delegate)genericMethod.Invoke(null, new object[] { options })!;
        }
    }

    private bool IsDynamicFlowEntity(Type type)
    {
        return typeof(IDynamicFlowEntity).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract;
    }

    private Delegate CreateValueObjectHandler(Type valueObjectType, AutoFlowOptions options)
    {
        // Create a closure that captures the options
        var handlerType = typeof(Func<,>).MakeGenericType(valueObjectType, typeof(Task));
        var method = typeof(FlowHandlerConfigurator).GetMethod(nameof(CreateValueObjectHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Static);
        var genericMethod = method!.MakeGenericMethod(valueObjectType);
        return (Delegate)genericMethod.Invoke(null, new object[] { options })!;
    }

    private static Func<TModel, Task> CreateEntityHandlerGeneric<TModel>(AutoFlowOptions options) 
        where TModel : FlowEntity<TModel>, new()
    {
        return async entity =>
        {
            if (options.EnableLogging)
            {
                var typeName = typeof(TModel).Name;
                var keyInfo = GetEntityKeyInfo(entity);
                Console.WriteLine(string.Format(options.EntityLogFormat, typeName, keyInfo));
            }

            try
            {
                // Direct intake via IFlowSender (avoid re-publishing plain entity messages which have no consumers)
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var sender = sp?.GetService<IFlowSender>();
                if (sender != null)
                {
                    var bag = ToBag(entity);
                    var item = FlowSendPlainItem.Of<TModel>(bag, sourceId: "auto-handler", occurredAt: DateTimeOffset.UtcNow);
                    // Don't pass entity type as hostType - that's for adapter classes with [FlowAdapter]
                    await sender.SendAsync(new[] { item }, message: null, hostType: null);
                }
                else
                {
                    // Fallback (no sender registered) – do NOT re-send to messaging to avoid backlog loop
                    System.Diagnostics.Debug.WriteLine($"⚠️  IFlowSender not available for {typeof(TModel).Name}; intake skipped.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  Flow auto handler failed to intake {typeof(TModel).Name}: {ex.Message}");
                throw;
            }
        };
    }

    private static Func<TModel, Task> CreateDynamicEntityHandlerGeneric<TModel>(AutoFlowOptions options) 
        where TModel : class, IDynamicFlowEntity, new()
    {
        return async entity =>
        {
            if (options.EnableLogging)
            {
                var typeName = typeof(TModel).Name;
                var keyInfo = GetDynamicEntityKeyInfo(entity);
                Console.WriteLine(string.Format(options.EntityLogFormat, typeName, keyInfo));
            }

            try
            {
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var sender = sp?.GetService<IFlowSender>();
                if (sender != null)
                {
                    var bag = ToBag(entity);
                    var item = FlowSendPlainItem.Of<TModel>(bag, sourceId: "auto-handler", occurredAt: DateTimeOffset.UtcNow);
                    // Don't pass entity type as hostType - that's for adapter classes with [FlowAdapter]
                    await sender.SendAsync(new[] { item }, message: null, hostType: null);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️  IFlowSender not available for dynamic {typeof(TModel).Name}; intake skipped.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  Flow auto handler failed to intake dynamic {typeof(TModel).Name}: {ex.Message}");
                throw;
            }
        };
    }

    private static Func<TValueObject, Task> CreateValueObjectHandlerGeneric<TValueObject>(AutoFlowOptions options) 
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        return async valueObject =>
        {
            if (options.EnableLogging)
            {
                var typeName = typeof(TValueObject).Name;
                var keyInfo = GetValueObjectKeyInfo(valueObject);
                Console.WriteLine(string.Format(options.ValueObjectLogFormat, typeName, keyInfo));
            }

            try
            {
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var sender = sp?.GetService<IFlowSender>();
                if (sender != null)
                {
                    var bag = ToBag(valueObject);
                    var item = FlowSendPlainItem.Of<TValueObject>(bag, sourceId: "auto-handler", occurredAt: DateTimeOffset.UtcNow);
                    await sender.SendAsync(new[] { item }, message: null, hostType: typeof(TValueObject));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️  IFlowSender not available for {typeof(TValueObject).Name} value object; intake skipped.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  Flow auto handler failed to intake value object {typeof(TValueObject).Name}: {ex.Message}");
                throw;
            }
        };
    }

    // Simple property bag extraction (duplicates logic in SoraAutoRegistrar to avoid dependency tangle)
    private static System.Collections.Generic.IDictionary<string, object?> ToBag(object entity)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;
        try
        {
            // Special handling for DynamicFlowEntity - extract from Model property
            if (entity is IDynamicFlowEntity dynamicEntity && dynamicEntity.Model != null)
            {
                // Flatten the ExpandoObject Model to dictionary paths
                var flattened = FlattenExpando(dynamicEntity.Model, "");
                foreach (var kvp in flattened)
                {
                    dict[kvp.Key] = kvp.Value;
                }
                // Also add the Id if present
                var idProp = entity.GetType().GetProperty("Id");
                if (idProp != null && idProp.CanRead)
                {
                    var idVal = idProp.GetValue(entity);
                    if (idVal != null) dict["Id"] = idVal;
                }
            }
            else
            {
                // Regular entity - extract simple properties
                var props = entity.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                foreach (var p in props)
                {
                    if (!p.CanRead) continue;
                    var val = p.GetValue(entity);
                    if (val is null || IsSimple(val.GetType()))
                    {
                        dict[p.Name] = val;
                    }
                }
            }
        }
        catch { }
        return dict;
    }
    
    // Helper to flatten ExpandoObject to dotted paths
    private static System.Collections.Generic.Dictionary<string, object?> FlattenExpando(System.Dynamic.ExpandoObject expando, string prefix)
    {
        var result = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dict = (System.Collections.Generic.IDictionary<string, object?>)expando;
        
        foreach (var kvp in dict)
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";
            
            if (kvp.Value is System.Dynamic.ExpandoObject nested)
            {
                // Recursively flatten nested ExpandoObjects
                var nestedFlattened = FlattenExpando(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[currentPath] = kvp.Value;
            }
        }
        
        return result;
    }

    private static bool IsSimple(Type t)
    {
        if (t.IsPrimitive || t.IsEnum) return true;
        return t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid) || t == typeof(TimeSpan);
    }

    private static string GetEntityKeyInfo<TModel>(TModel entity) where TModel : FlowEntity<TModel>, new()
    {
        try
        {
            // Look for [Key] property for primary identifier
            var keyProp = typeof(TModel).GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);
            
            if (keyProp != null)
            {
                var keyValue = keyProp.GetValue(entity);
                if (keyValue != null) return $"{keyProp.Name}: {keyValue}";
            }

            // Fallback to Id property
            var idValue = entity.Id;
            if (!string.IsNullOrWhiteSpace(idValue)) return $"Id: {idValue}";
            
            return "received";
        }
        catch
        {
            return "received";
        }
    }

    private static string GetValueObjectKeyInfo<TValueObject>(TValueObject valueObject) where TValueObject : FlowValueObject<TValueObject>, new()
    {
        try
        {
            // Look for properties that might be identifiers (common patterns)
            var props = typeof(TValueObject).GetProperties()
                .Where(p => p.CanRead)
                .ToArray();

            // Look for Key, SensorKey, DeviceId, etc.
            var keyProp = props.FirstOrDefault(p => 
                p.Name.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
                p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);

            if (keyProp != null)
            {
                var keyValue = keyProp.GetValue(valueObject);
                if (keyValue != null) return $"{keyProp.Name}: {keyValue}";
            }

            // For value objects like Reading, show meaningful properties
            if (typeof(TValueObject).Name == "Reading")
            {
                var sensorProp = props.FirstOrDefault(p => p.Name == "SensorKey");
                var valueProp = props.FirstOrDefault(p => p.Name == "Value");
                var unitProp = props.FirstOrDefault(p => p.Name == "Unit");

                if (sensorProp != null && valueProp != null && unitProp != null)
                {
                    var sensor = sensorProp.GetValue(valueObject);
                    var value = valueProp.GetValue(valueObject);
                    var unit = unitProp.GetValue(valueObject);
                    return $"{sensor} = {value}{unit}";
                }
            }

            return "received";
        }
        catch
        {
            return "received";
        }
    }
    
    private static string GetDynamicEntityKeyInfo<TModel>(TModel entity) where TModel : class
    {
        try
        {
            // For DynamicFlowEntity types, look for Id property first
            var idProp = typeof(TModel).GetProperty("Id");
            if (idProp != null)
            {
                var idValue = idProp.GetValue(entity);
                if (idValue != null) return $"Id: {idValue}";
            }
            

            return "received";
        }
        catch
        {
            return "received";
        }
    }
}