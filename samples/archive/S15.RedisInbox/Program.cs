using Koan.Core;
using Koan.Core.Observability;
using Koan.Service.Inbox.Connector.Redis;
using Koan.Web.Connector.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

builder.Services.AddKoanObservability();

var app = builder.Build();

app.UseKoanSwagger();

app.Run();

namespace S15.RedisInbox
{
    public partial class Program { }
}
