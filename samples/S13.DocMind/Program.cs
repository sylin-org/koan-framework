using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Web.Extensions;
using S13.DocMind.Services;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework initialization
builder.Services.AddKoan();

// Add Koan observability for proper startup sequence logging
builder.Services.AddKoanObservability();

// Register DocMind-specific processing pipeline
builder.Services.AddDocMindProcessing(builder.Configuration);

// Note: Service implementations are handled by Koan auto-registration

// Ensure required directories exist
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "uploads"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"));

var app = builder.Build();

// Koan.Web startup filter auto-wires static files, controller routing, and Swagger

app.Run();