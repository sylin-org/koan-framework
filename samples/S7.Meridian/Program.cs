using Koan;
using Koan.Samples.Meridian;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework Bootstrap
builder.Services.AddKoan();

var app = builder.Build();

// Koan Environment Info
if (KoanEnv.IsDevelopment)
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Meridian API v1"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
