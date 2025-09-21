using Microsoft.AspNetCore.Mvc;
using S5.Recs.Models;
using Koan.Web;
using Koan.Web.Controllers;

namespace S5.Recs.Controllers;

// MediaController is defined in MediaController.cs, removed duplicate

[ApiController]
[Route("api/data/library")]
public class LibraryEntryController : EntityController<LibraryEntry, string> { }

[ApiController]
[Route("api/data/users")]
public class UserDocController : EntityController<UserDoc, string> { }

[ApiController]
[Route("api/data/profiles")]
public class UserProfileDocController : EntityController<UserProfileDoc, string> { }

[ApiController]
[Route("api/data/genres")]
public class GenreStatDocController : EntityController<GenreStatDoc, string> { }

[ApiController]
[Route("api/data/tags")]
public class TagStatDocController : EntityController<TagStatDoc, string> { }

[ApiController]
[Route("api/data/censoredtags")]
public class CensorTagsDocController : EntityController<CensorTagsDoc, string> { }

[ApiController]
[Route("api/data/settings")]
public class SettingsDocController : EntityController<SettingsDoc, string> { }
