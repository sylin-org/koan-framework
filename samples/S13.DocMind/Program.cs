using Koan.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework initialization
builder.Services.AddKoan();

// Note: Service implementations are handled by Koan auto-registration

// Ensure required directories exist
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "uploads"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "data"));

var app = builder.Build();

// Koan.Web startup filter auto-wires static files, controller routing, and Swagger

app.Run();
