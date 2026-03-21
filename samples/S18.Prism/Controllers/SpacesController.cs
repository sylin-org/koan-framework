using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S18.Prism.Models;

namespace S18.Prism.Controllers;

[Route("api/[controller]")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 50, MaxSize = 100, DefaultSort = "name")]
public class SpacesController : EntityController<Space>
{
}
