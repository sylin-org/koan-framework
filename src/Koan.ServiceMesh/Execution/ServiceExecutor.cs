using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.ServiceMesh.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.ServiceMesh.Execution;

/// <summary>
/// Routes service invocations to appropriate instances (in-process or remote).
/// Handles HTTP-based remote invocations and direct in-process calls.
/// </summary>
public class ServiceExecutor<TService> where TService : class
{
    private readonly IKoanServiceMesh _serviceMesh;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServiceExecutor<TService>> _logger;
    private readonly string _serviceId;

    public ServiceExecutor(
        IKoanServiceMesh serviceMesh,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<ServiceExecutor<TService>> logger,
        string serviceId)
    {
        _serviceMesh = serviceMesh;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _serviceId = serviceId;
    }

    /// <summary>
    /// Execute a service invocation with automatic routing.
    /// Prefers in-process if available, otherwise routes to remote instance.
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        string capability,
        object? parameters = null,
        LoadBalancingPolicy policy = LoadBalancingPolicy.RoundRobin,
        CancellationToken ct = default)
    {
        // Try in-process first
        var inProcessService = _serviceProvider.GetService<TService>();
        if (inProcessService != null)
        {
            _logger.LogDebug(
                "Koan:services:execute {Service}.{Capability} in-process",
                _serviceId,
                capability);

            return await ExecuteInProcessAsync<TResult>(
                inProcessService,
                capability,
                parameters,
                ct);
        }

        // Fallback to remote instance
        var instance = _serviceMesh.GetInstance(_serviceId, policy);
        if (instance == null)
        {
            throw new InvalidOperationException(
                $"No available instances found for service '{_serviceId}'. " +
                $"Ensure the service is running and discoverable.");
        }

        _logger.LogDebug(
            "Koan:services:execute {Service}.{Capability} remote at {Endpoint}",
            _serviceId,
            capability,
            instance.HttpEndpoint);

        return await ExecuteRemoteAsync<TResult>(instance, capability, parameters, ct);
    }

    /// <summary>
    /// Execute service method in-process using reflection.
    /// </summary>
    private async Task<TResult> ExecuteInProcessAsync<TResult>(
        TService service,
        string capability,
        object? parameters,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Find method by capability name (convert kebab-case to PascalCase)
            var methodName = ConvertCapabilityToMethodName(capability);
            var method = typeof(TService).GetMethod(methodName);

            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Method '{methodName}' not found on service '{typeof(TService).Name}'. " +
                    $"Capability: '{capability}'");
            }

            // Build parameter array
            var methodParams = method.GetParameters();
            var args = new List<object?>();

            if (parameters != null && methodParams.Length > 0)
            {
                // If parameters is a single object matching first parameter type, use directly
                if (methodParams.Length == 1 &&
                    methodParams[0].ParameterType.IsAssignableFrom(parameters.GetType()))
                {
                    args.Add(parameters);
                }
                else
                {
                    // Try to deserialize parameters to match method signature
                    var json = JsonSerializer.Serialize(parameters);
                    var deserializedParam = JsonSerializer.Deserialize(
                        json,
                        methodParams[0].ParameterType);
                    args.Add(deserializedParam);
                }
            }

            // Add CancellationToken if method expects it
            if (methodParams.Length > 0 &&
                methodParams[^1].ParameterType == typeof(CancellationToken))
            {
                args.Add(ct);
            }

            // Invoke method
            var result = method.Invoke(service, args.ToArray());

            // Handle async Task<T> return type
            if (result is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                var taskResult = resultProperty?.GetValue(task);

                sw.Stop();
                _logger.LogInformation(
                    "Koan:services:execute {Service}.{Capability} completed in {Ms}ms (in-process)",
                    _serviceId,
                    capability,
                    sw.ElapsedMilliseconds);

                return (TResult)taskResult!;
            }

            sw.Stop();
            _logger.LogInformation(
                "Koan:services:execute {Service}.{Capability} completed in {Ms}ms (in-process)",
                _serviceId,
                capability,
                sw.ElapsedMilliseconds);

            return (TResult)result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Koan:services:execute {Service}.{Capability} failed (in-process)",
                _serviceId,
                capability);
            throw;
        }
    }

    /// <summary>
    /// Execute service invocation via HTTP to remote instance.
    /// </summary>
    private async Task<TResult> ExecuteRemoteAsync<TResult>(
        ServiceInstance instance,
        string capability,
        object? parameters,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Track connection for load balancing metrics
            instance.ActiveConnections++;

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(instance.HttpEndpoint);

            // Build request URL: /api/{serviceId}/{capability}
            var url = $"/api/{_serviceId}/{capability}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = parameters != null
                    ? JsonContent.Create(parameters)
                    : null
            };

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TResult>(
                cancellationToken: ct);

            sw.Stop();

            // Update metrics
            instance.ActiveConnections--;
            instance.AverageResponseTime = TimeSpan.FromMilliseconds(
                (instance.AverageResponseTime.TotalMilliseconds * 0.8) +
                (sw.Elapsed.TotalMilliseconds * 0.2)); // Exponential moving average

            _logger.LogInformation(
                "Koan:services:execute {Service}.{Capability} completed in {Ms}ms (remote {Instance})",
                _serviceId,
                capability,
                sw.ElapsedMilliseconds,
                instance.InstanceId);

            return result!;
        }
        catch (Exception ex)
        {
            instance.ActiveConnections--;
            instance.Status = ServiceInstanceStatus.Degraded;

            _logger.LogError(
                ex,
                "Koan:services:execute {Service}.{Capability} failed (remote {Instance})",
                _serviceId,
                capability,
                instance.InstanceId);

            throw;
        }
    }

    /// <summary>
    /// Convert kebab-case capability name to PascalCase method name.
    /// Examples: "translate" → "Translate", "detect-language" → "DetectLanguage"
    /// </summary>
    private static string ConvertCapabilityToMethodName(string capability)
    {
        var parts = capability.Split('-');
        return string.Join("", parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
    }
}
