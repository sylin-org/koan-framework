using System;
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
    /// Usage: services.ConfigureFlow(flow => flow.On<Device>(device => ProcessDevice(device)))
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
    /// Default: "🏭 {TypeName} {KeyInfo}"
    /// </summary>
    public string EntityLogFormat { get; set; } = "🏭 {0} {1}";

    /// <summary>
    /// Custom format for value object log messages. Parameters: {TypeName}, {KeyInfo}
    /// Default: "📊 {TypeName} {KeyInfo}"
    /// </summary>
    public string ValueObjectLogFormat { get; set; } = "📊 {0} {1}";

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
    /// Usage: flow.On<Device>(device => ProcessDevice(device))
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

        Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Scanning {assemblies.Length} assemblies for FlowEntity/FlowValueObject types");
        int totalHandlersRegistered = 0;
        
        foreach (var assembly in assemblies)
        {
            Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Scanning assembly: {assembly.FullName}");
            var types = GetTypesFromAssembly(assembly);
            Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Found {types.Length} types in assembly");
            
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
                    Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Found DynamicFlowEntity: {type.Name} -> registering dynamic handler");
                    RegisterDynamicFlowEntityHandler(type, dynamicEntityBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }

                // Check if it's a FlowEntity<T>
                var entityBaseType = GetFlowEntityBaseType(type);
                if (entityBaseType != null)
                {
                    Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Found FlowEntity: {type.Name} -> registering handler");
                    RegisterFlowEntityHandler(type, entityBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }

                // Check if it's a FlowValueObject<T>
                var valueObjectBaseType = GetFlowValueObjectBaseType(type);
                if (valueObjectBaseType != null)
                {
                    Console.WriteLine($"🔧 AUTO-CONFIG DEBUG: Found FlowValueObject: {type.Name} -> registering handler");
                    RegisterFlowValueObjectHandler(type, valueObjectBaseType, options);
                    totalHandlersRegistered++;
                    continue;
                }
            }
        }

        Console.WriteLine($"🎯 AUTO-CONFIG SUMMARY: Successfully registered {totalHandlersRegistered} handlers through AutoConfigureFlow");
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
        Console.WriteLine($"🔧 REGISTER DEBUG: Starting FlowEntity handler registration for {entityType.Name}");
        
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        Console.WriteLine($"🔧 REGISTER DEBUG: Found {onMethods.Length} On<T> method candidates for {entityType.Name}");
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            Console.WriteLine($"🔧 REGISTER DEBUG: Checking method with parameter type: {parameterType}");
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            Console.WriteLine($"❌ REGISTER ERROR: Could not find compatible On<T> method for {entityType.Name}");
            return;
        }
        Console.WriteLine($"✅ REGISTER DEBUG: Found On<T> method for {entityType.Name}");

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(entityType);
        Console.WriteLine($"✅ REGISTER DEBUG: Created generic On<{entityType.Name}> method");

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(entityType, typeof(Task));
        var handler = CreateEntityHandler(entityType, options);
        Console.WriteLine($"✅ REGISTER DEBUG: Created handler delegate for {entityType.Name}");

        // Invoke On<EntityType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
            Console.WriteLine($"🎯 REGISTER SUCCESS: Successfully registered FlowEntity handler for {entityType.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ REGISTER ERROR: Failed to register FlowEntity handler for {entityType.Name}: {ex.Message}");
        }
    }

    private void RegisterDynamicFlowEntityHandler(Type entityType, Type baseType, AutoFlowOptions options)
    {
        Console.WriteLine($"🔧 REGISTER DEBUG: Starting DynamicFlowEntity handler registration for {entityType.Name}");
        
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        Console.WriteLine($"🔧 REGISTER DEBUG: Found {onMethods.Length} On<T> method candidates for {entityType.Name}");
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            Console.WriteLine($"🔧 REGISTER DEBUG: Checking method with parameter type: {parameterType}");
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            Console.WriteLine($"❌ REGISTER ERROR: Could not find compatible On<T> method for {entityType.Name}");
            return;
        }
        Console.WriteLine($"✅ REGISTER DEBUG: Found On<T> method for {entityType.Name}");

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(entityType);
        Console.WriteLine($"✅ REGISTER DEBUG: Created generic On<{entityType.Name}> method");

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(entityType, typeof(Task));
        var handler = CreateEntityHandler(entityType, options);
        Console.WriteLine($"✅ REGISTER DEBUG: Created handler delegate for {entityType.Name}");

        // Invoke On<EntityType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
            Console.WriteLine($"🎯 REGISTER SUCCESS: Successfully registered DynamicFlowEntity handler for {entityType.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ REGISTER ERROR: Failed to register DynamicFlowEntity handler for {entityType.Name}: {ex.Message}");
        }
    }

    private void RegisterFlowValueObjectHandler(Type valueObjectType, Type baseType, AutoFlowOptions options)
    {
        Console.WriteLine($"🔧 REGISTER DEBUG: Starting FlowValueObject handler registration for {valueObjectType.Name}");
        
        // Create handler method using reflection
        var onMethods = typeof(FlowHandlerConfigurator).GetMethods()
            .Where(m => m.Name == nameof(On) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToArray();
        
        Console.WriteLine($"🔧 REGISTER DEBUG: Found {onMethods.Length} On<T> method candidates for {valueObjectType.Name}");
        
        var onMethod = onMethods.FirstOrDefault(m => 
        {
            var parameterType = m.GetParameters()[0].ParameterType;
            Console.WriteLine($"🔧 REGISTER DEBUG: Checking method with parameter type: {parameterType}");
            return parameterType.IsGenericType && 
                   parameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                   parameterType.GetGenericArguments()[1] == typeof(Task);
        });
        
        if (onMethod == null) 
        {
            Console.WriteLine($"❌ REGISTER ERROR: Could not find compatible On<T> method for {valueObjectType.Name}");
            return;
        }
        Console.WriteLine($"✅ REGISTER DEBUG: Found On<T> method for {valueObjectType.Name}");

        // Create the generic method
        var genericMethod = onMethod.MakeGenericMethod(valueObjectType);
        Console.WriteLine($"✅ REGISTER DEBUG: Created generic On<{valueObjectType.Name}> method");

        // Create the handler delegate
        var handlerType = typeof(Func<,>).MakeGenericType(valueObjectType, typeof(Task));
        var handler = CreateValueObjectHandler(valueObjectType, options);
        Console.WriteLine($"✅ REGISTER DEBUG: Created handler delegate for {valueObjectType.Name}");

        // Invoke On<ValueObjectType>(handler)
        try
        {
            genericMethod.Invoke(this, new object[] { handler });
            Console.WriteLine($"🎯 REGISTER SUCCESS: Successfully registered FlowValueObject handler for {valueObjectType.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ REGISTER ERROR: Failed to register FlowValueObject handler for {valueObjectType.Name}: {ex.Message}");
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
            Console.WriteLine($"🏭 AUTO-HANDLER DEBUG: Processing FlowEntity<{typeof(TModel).Name}> at {DateTime.Now:HH:mm:ss.fff}");
            
            if (options.EnableLogging)
            {
                var typeName = typeof(TModel).Name;
                var keyInfo = GetEntityKeyInfo(entity);
                Console.WriteLine(string.Format(options.EntityLogFormat, typeName, keyInfo));
            }
            
            try
            {
                // Route to Flow intake for processing
                Console.WriteLine($"🏭 AUTO-HANDLER DEBUG: Sending {typeof(TModel).Name} to Flow intake");
                await entity.SendToFlowIntake();
                Console.WriteLine($"✅ AUTO-HANDLER DEBUG: Successfully processed {typeof(TModel).Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AUTO-HANDLER ERROR: Failed to process {typeof(TModel).Name}: {ex.Message}");
                throw;
            }
        };
    }

    private static Func<TModel, Task> CreateDynamicEntityHandlerGeneric<TModel>(AutoFlowOptions options) 
        where TModel : class, IDynamicFlowEntity, new()
    {
        return async entity =>
        {
            Console.WriteLine($"🏭 AUTO-HANDLER DEBUG: Processing DynamicFlowEntity<{typeof(TModel).Name}> at {DateTime.Now:HH:mm:ss.fff}");
            
            if (options.EnableLogging)
            {
                var typeName = typeof(TModel).Name;
                var keyInfo = GetDynamicEntityKeyInfo(entity);
                Console.WriteLine(string.Format(options.EntityLogFormat, typeName, keyInfo));
            }
            
            try
            {
                // Route to Flow intake directly (bypass messaging loop)
                Console.WriteLine($"🏭 AUTO-HANDLER DEBUG: Sending DynamicFlowEntity {typeof(TModel).Name} to Flow intake");
                await entity.SendToFlowIntake();
                Console.WriteLine($"✅ AUTO-HANDLER DEBUG: Successfully processed DynamicFlowEntity {typeof(TModel).Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AUTO-HANDLER ERROR: Failed to process DynamicFlowEntity {typeof(TModel).Name}: {ex.Message}");
                throw;
            }
        };
    }

    private static Func<TValueObject, Task> CreateValueObjectHandlerGeneric<TValueObject>(AutoFlowOptions options) 
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        return async valueObject =>
        {
            Console.WriteLine($"📊 AUTO-HANDLER DEBUG: Processing FlowValueObject<{typeof(TValueObject).Name}> at {DateTime.Now:HH:mm:ss.fff}");
            
            if (options.EnableLogging)
            {
                var typeName = typeof(TValueObject).Name;
                var keyInfo = GetValueObjectKeyInfo(valueObject);
                Console.WriteLine(string.Format(options.ValueObjectLogFormat, typeName, keyInfo));
            }
            
            try
            {
                // Route to Flow intake for processing
                Console.WriteLine($"📊 AUTO-HANDLER DEBUG: Sending {typeof(TValueObject).Name} to Flow intake");
                await valueObject.SendToFlowIntake();
                Console.WriteLine($"✅ AUTO-HANDLER DEBUG: Successfully processed {typeof(TValueObject).Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AUTO-HANDLER ERROR: Failed to process {typeof(TValueObject).Name}: {ex.Message}");
                throw;
            }
        };
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
            
            // Look for CanonicalId property (specific to DynamicFlowEntity)
            var canonicalIdProp = typeof(TModel).GetProperty("CanonicalId");
            if (canonicalIdProp != null)
            {
                var canonicalValue = canonicalIdProp.GetValue(entity);
                if (canonicalValue != null) return $"CanonicalId: {canonicalValue}";
            }

            return "received";
        }
        catch
        {
            return "received";
        }
    }
}