using Koan.Data.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Koan web defaults
builder.Services.AddKoan().AsWebApi().WithExceptionHandler();

// Koan.Web.Auth is auto-registered via Koan auto-bootstrap; explicit call not required.

var app = builder.Build();

// Static files are enabled by Koan.Web initialization; no explicit calls needed.

app.Run();

namespace S6.Auth
{
    public partial class Program { }
}
