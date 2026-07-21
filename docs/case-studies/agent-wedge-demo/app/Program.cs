using Koan.Core;

var builder = WebApplication.CreateBuilder(args);

// The whole bootstrap. AddKoan() reflectively discovers every referenced pillar (Reference = Intent):
// Koan.Web maps the controllers, the Sqlite connector registers its adapter — no per-feature wiring.
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
