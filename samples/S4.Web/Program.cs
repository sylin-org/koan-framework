using Sora.Data.Core;
using Sora.Core.Observability;
using Sora.Web;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddSoraObservability();

var app = builder.Build();
app.UseSoraSwagger();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

public partial class Program { }
