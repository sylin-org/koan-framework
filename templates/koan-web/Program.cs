using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();          // Reference = Intent: Web + Sqlite auto-register from the package refs
var app = builder.Build();
app.Run();                           // TodosController : EntityController<Todo> is auto-mapped at /api/todos
