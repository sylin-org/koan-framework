using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Flow.Model;
using S8.Flow.Shared;
using Koan.Data.Core;

namespace S8.Flow.Api.Controllers;

[ApiController]
[Route("api/devices")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 20, MaxPageSize = 200)]
public sealed class DevicesController : EntityController<DynamicFlowEntity<Device>>
{
}
