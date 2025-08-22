using Sora.Data.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();

namespace S4.Web
{
    public partial class Program { }
}
