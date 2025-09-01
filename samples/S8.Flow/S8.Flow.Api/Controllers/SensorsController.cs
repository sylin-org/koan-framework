using Microsoft.AspNetCore.Mvc;
using Sora.Web.Attributes;
using Sora.Web.Controllers;
using Sora.Flow.Model;
using S8.Flow.Shared;

namespace S8.Flow.Api.Controllers;

[Route("api/sensors")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 20, MaxPageSize = 200)]
public sealed class SensorsController : EntityController<DynamicFlowEntity<Sensor>> { }
