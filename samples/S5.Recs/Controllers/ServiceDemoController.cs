using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Services.Attributes;
using Koan.Web.Auth.Services.Http;

namespace S5.Recs.Controllers;

[ApiController]
[KoanService("s5-recs-backend", ProvidedScopes = new[] { "recommendations:read", "recommendations:write" })]
[Route("api/[controller]")]
public class ServiceDemoController : ControllerBase
{
    private readonly IKoanServiceClient _serviceClient;
    private readonly ILogger<ServiceDemoController> _logger;

    public ServiceDemoController(IKoanServiceClient serviceClient, ILogger<ServiceDemoController> logger)
    {
        _serviceClient = serviceClient;
        _logger = logger;
    }

    [HttpGet("test-service-auth")]
    [CallsService("ai-service", RequiredScopes = new[] { "ml:inference" })]
    public async Task<IActionResult> TestServiceAuth()
    {
        _logger.LogInformation("Testing service authentication...");

        try
        {
            // This would normally call an AI service for ML inference
            // For demo purposes, we'll simulate the call
            var mockAiRequest = new { prompt = "Hello AI", maxTokens = 100 };

            // The service client will automatically:
            // 1. Resolve the ai-service endpoint (localhost:8001, docker service, etc.)
            // 2. Acquire a JWT token with "ml:inference" scope
            // 3. Add Authorization: Bearer <token> header
            // 4. Make the HTTP call

            // Simulated service call - would normally be:
            // var result = await _serviceClient.PostAsync<AiResponse>("ai-service", "/api/inference", mockAiRequest);

            _logger.LogInformation("Service call would be made to ai-service with JWT authentication");

            return Ok(new
            {
                message = "Service authentication test successful",
                serviceId = "ai-service",
                requiredScopes = new[] { "ml:inference" },
                request = mockAiRequest,
                note = "In a real scenario, this would call the AI service with automatic JWT authentication"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service authentication test failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("multi-service-call")]
    [CallsService("ai-service", RequiredScopes = new[] { "ml:inference" })]
    [CallsService("analytics-service", RequiredScopes = new[] { "analytics:write" }, Optional = true)]
    public async Task<IActionResult> MultiServiceCall([FromBody] ProcessingRequest request)
    {
        _logger.LogInformation("Processing request with multiple service calls");

        var results = new List<object>();

        try
        {
            // Required service call to AI service
            _logger.LogInformation("Calling AI service for inference...");
            // var aiResult = await _serviceClient.PostAsync<InferenceResult>("ai-service", "/api/inference", request.AiRequest);
            results.Add(new { service = "ai-service", status = "success", note = "Would contain AI inference result" });

            // Optional service call to analytics service
            try
            {
                _logger.LogInformation("Calling analytics service for event tracking...");
                // var analyticsResult = await _serviceClient.PostAsync<AnalyticsResult>("analytics-service", "/api/events", request.AnalyticsEvent);
                results.Add(new { service = "analytics-service", status = "success", note = "Would track analytics event" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Optional analytics service call failed");
                results.Add(new { service = "analytics-service", status = "failed", error = ex.Message, note = "Optional service - processing continues" });
            }

            return Ok(new
            {
                message = "Multi-service processing completed",
                results = results,
                processedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Required service call failed");
            return StatusCode(500, new { error = "Required service call failed: " + ex.Message });
        }
    }

    public record ProcessingRequest(object AiRequest, object AnalyticsEvent);
}