using Koan.Core;
using Koan.Data.Core;
using Koan.Orchestration.Aspire;
using Koan.Orchestration.Aspire.SelfOrchestration;

var builder = WebApplication.CreateBuilder(args);


// Add Koan services - this will automatically register all referenced modules
builder.Services.AddKoan();

// Add controllers and API explorer
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Add a simple health check endpoint
app.MapGet("/health", () => "Healthy");

app.Run();