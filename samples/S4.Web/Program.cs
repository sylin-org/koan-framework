using Sora.Data.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();

var app = builder.Build();

// Static files are wired by Sora.Web; no explicit calls needed here.
app.Run();

namespace S4.Web
{
    public partial class Program { }
}
