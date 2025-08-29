using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Models;
using Sora.Web.Extensions.Controllers;

namespace S7.TechDocs.Controllers;

// Exposes moderation endpoints for Document via the generic controller:
//  POST   /api/documents/{id}/moderation/draft      (create)
//  PATCH  /api/documents/{id}/moderation/draft      (update)
//  GET    /api/documents/{id}/moderation/draft      (get)
//  POST   /api/documents/{id}/moderation/submit     (submit)
//  POST   /api/documents/{id}/moderation/withdraw   (withdraw)
//  GET    /api/documents/moderation/queue           (review queue)
//  POST   /api/documents/{id}/moderation/approve    (approve)
//  POST   /api/documents/{id}/moderation/reject     (reject)
//  POST   /api/documents/{id}/moderation/return     (return)
[Route("api/documents")]
public class DocumentModerationController : EntityModerationController<Document>
{ }
