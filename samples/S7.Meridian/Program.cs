using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Koan Framework Bootstrap
builder.Services
    .AddKoan()
    .AsWebApi();

var app = builder.Build();

AppHost.Current ??= app.Services;

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
