using AnimeRecommendations.Domain;
using AnimeRecommendations.Infrastructure;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace AnimeRecommendations.Controllers;

[Route(AnimeRecommendationsConstants.Routes.Catalog)]
[Pagination(Mode = PaginationMode.Off)]
public sealed class AnimeController : EntityController<Anime>;

[Route(AnimeRecommendationsConstants.Routes.Viewers)]
[Pagination(Mode = PaginationMode.Off)]
public sealed class ViewersController : EntityController<Viewer>;

[Route(AnimeRecommendationsConstants.Routes.Library)]
[Pagination(Mode = PaginationMode.Off)]
public sealed class LibraryController : EntityController<LibraryEntry>;
