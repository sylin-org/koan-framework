using g1c1.GardenCoop.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace g1c1.GardenCoop.Controllers;

// EntityController<Reminder> gives us full CRUD for free
// dashboard uses GET to fetch active reminders
// members use PATCH to acknowledge after watering
[Route("api/garden/reminders")]
public sealed class RemindersController : EntityController<Reminder>
{
    // the automation in GardenAutomation creates and updates these automatically
}
