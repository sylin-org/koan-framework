using Koan.Core;
using Koan.Web;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using S6.SnapVault.Configuration;
using S6.SnapVault.Services;
using S6.SnapVault.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for large file uploads
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 524288000; // 500MB for batch uploads
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
});

// Configure FormOptions for multipart form data
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Koan Framework - "Reference = Intent"
builder.Services
    .AddKoan()
    .AsWebApi();

// Configure entity lifecycle events (cascade deletes, etc.)
EntityLifecycleConfiguration.Configure();

// Register application services
builder.Services.AddScoped<IPhotoProcessingService, PhotoProcessingService>();

// Register background processing queue and worker
builder.Services.AddSingleton<IPhotoProcessingQueue, InMemoryPhotoProcessingQueue>();
builder.Services.AddHostedService<PhotoProcessingWorker>();

// SignalR for real-time progress updates
builder.Services.AddSignalR();

// CORS for development (allow credentials for SignalR)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    // TODO: Add Swagger package if needed
    // app.UseSwagger();
    // app.UseSwaggerUI(c =>
    // {
    //     c.SwaggerEndpoint("/swagger/v1/swagger.json", "SnapVault API v1");
    //     c.RoutePrefix = "swagger";
    // });
}

app.UseCors();
app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapFallbackToFile("index.html");

// Map SignalR hubs
app.MapHub<PhotoProcessingHub>("/hubs/processing");

app.Run();
