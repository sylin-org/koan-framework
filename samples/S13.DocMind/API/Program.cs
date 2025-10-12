using Koan.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

var app = builder.Build();

app.Run();
