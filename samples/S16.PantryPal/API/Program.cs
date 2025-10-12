using Koan.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
builder.Services.AddMvc(options =>
{
    options.Filters.Add<S16.PantryPal.Infrastructure.ErrorEnvelopeFilter>();
});
var app = builder.Build();
app.Run();

namespace S16.PantryPal
{
    public partial class Program { }
}
