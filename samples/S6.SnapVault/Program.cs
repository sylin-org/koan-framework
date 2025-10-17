using Koan.Core;
using Koan.Web;
using Koan.Web.Extensions;
using S6.SnapVault.Configuration;
using S6.SnapVault.Services;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework - "Reference = Intent"
builder.Services
    .AddKoan()
    .AsWebApi();

// Configure entity lifecycle events (cascade deletes, etc.)
EntityLifecycleConfiguration.Configure();

// Register application services
builder.Services.AddScoped<IPhotoProcessingService, PhotoProcessingService>();

// SignalR for real-time progress updates
builder.Services.AddSignalR();

// CORS for development
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
app.MapHub<ProcessingHub>("/hubs/processing");

app.Run();

// SignalR Hub for processing progress
public class ProcessingHub : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task JoinEventGroup(string eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, eventId);
    }

    public async Task LeaveEventGroup(string eventId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId);
    }
}
