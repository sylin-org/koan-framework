using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Models;
using Koan.Web.Controllers;

namespace S7.TechDocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : EntityController<Document> { }
