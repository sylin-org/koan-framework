using Koan.Core;
using Koan.Data.Core;
using Koan.Orchestration.Aspire;
using Koan.Orchestration.Aspire.SelfOrchestration;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Koan services - this will automatically register all referenced modules
builder.Services.AddKoan();

builder.Services.AddKoanSwagger(builder.Configuration);

var app = builder.Build();

app.UseKoanSwagger();

app.Run();