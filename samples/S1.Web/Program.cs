using Sora.Data.Core;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Web;
using S1.Web;

var builder = WebApplication.CreateBuilder(args);

// Framework bootstrap
builder.Services.AddSora()
	// Sensible defaults: controllers, static files, secure headers, ProblemDetails
	.AsWebApi()
	// Toggle middleware
	.WithExceptionHandler()
	.WithRateLimit();

// Transformers disabled in S1: keep API simple here.

// App policy: register a rate limiter (pipeline toggle enables UseRateLimiter)
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
		RateLimitPartition.GetFixedWindowLimiter(
			partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
			factory: _ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = 200,
				Window = TimeSpan.FromMinutes(1),
				QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
				QueueLimit = 0
			}));
});

// Optional: use SQLite instead of JSON
// builder.Services.AddSqliteAdapter(o => o.ConnectionString = "Data Source=.\\data\\s1.sqlite");

// Sora.Web wires routing, controllers, static files, secure headers, and /api/health.
var app = builder.Build();

// Platform auto-ensures schema at startup when supported

app.Run();

// Make Program public and partial to help WebApplicationFactory discovery in tests
public partial class Program { }