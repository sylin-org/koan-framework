using Sora.Data.Core;
using Sora.Core.Observability;
using Sora.Web;
using Sora.Web.Swagger;
using Sora.Web.GraphQl;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

public partial class Program { }
