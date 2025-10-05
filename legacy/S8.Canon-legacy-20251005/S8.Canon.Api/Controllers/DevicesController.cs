using Microsoft.AspNetCore.Mvc;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Canon.Model;
using S8.Canon.Shared;
using Koan.Data.Core;

namespace S8.Canon.Api.Controllers;

[ApiController]
[Route("api/devices")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 20, MaxPageSize = 200)]
public sealed class DevicesController : EntityController<DynamicCanonEntity<Device>>
{
}

