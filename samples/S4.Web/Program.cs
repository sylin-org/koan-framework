using Sora.Data.Core;
using Sora.Core.Observability;
using Sora.Web;
using Sora.Web.Swagger;
using Sora.Web.GraphQl;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddSoraObservability();
builder.Services.AddSoraGraphQl();

var app = builder.Build();
app.UseSoraSwagger();
app.UseSoraGraphQl();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

public partial class Program { }
