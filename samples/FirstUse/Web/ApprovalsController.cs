using Koan.FirstUse.Domain;
using Koan.FirstUse.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.FirstUse.Web;

[Route(FirstUseConstants.Routes.Approvals)]
public sealed class ApprovalsController : EntityController<Approval>;
