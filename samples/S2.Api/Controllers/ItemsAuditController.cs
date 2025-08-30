// Compiled out: replaced by generic controller registration in Program.cs
#if false
using Microsoft.AspNetCore.Mvc;
using S2.Api.Controllers;
using Sora.Web.Extensions.Controllers;

namespace S2.Api.Controllers;

// Audit endpoints for Item
[Route("api/items")]
public sealed class ItemsAuditController : EntityAuditController<Item>
{ }
#endif
