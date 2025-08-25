using Sora.Web.Extensions;
using Sora.Data.Core;

var builder = WebApplication.CreateBuilder(args);

// Sora web defaults
builder.Services.AddSora().AsWebApi().WithExceptionHandler();

// Sora.Web.Auth is auto-registered via Sora auto-bootstrap; explicit call not required.

var app = builder.Build();

// Static files are enabled by Sora.Web initialization; no explicit calls needed.

app.Run();

namespace S6.Auth
{
    public partial class Program { }
}
