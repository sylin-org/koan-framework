using GardenCoop.Infrastructure;
using GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GardenCoop.Controllers;

[Route(GardenApiRoutes.Reminders)]
public sealed class RemindersController : EntityController<Reminder>;
