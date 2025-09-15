using Koan.Data.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

var app = builder.Build();

// Static files are wired by Koan.Web; no explicit calls needed here.
app.Run();

namespace S4.Web
{
    public partial class Program { }
}
