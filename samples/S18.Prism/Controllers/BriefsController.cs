using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S18.Prism.Models;

namespace S18.Prism.Controllers;

[Route("api/[controller]")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 100, DefaultSort = "-id")]
public class BriefsController : EntityController<ResearchBrief>
{
}
