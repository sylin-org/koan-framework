using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace KoanSemSearchApp;

[Route("api/articles")]
public sealed class ArticlesController : EntityController<Article>;
