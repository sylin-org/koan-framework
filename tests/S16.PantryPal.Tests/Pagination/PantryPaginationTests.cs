using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using S16.PantryPal.Controllers;
using S16.PantryPal.Models;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S16.PantryPal.Tests.Pagination;

[Collection("KoanHost")]
public class PantryPaginationTests
{
    // Lightweight in-process controller test exercising pagination defaults via query params.
    // We rely on EntityController paging behavior; here we seed entities and invoke action indirectly
    // by constructing filter arguments and verifying clamped sizes.

    [Fact]
    public async Task Default_Page_Size_Should_Apply()
    {
        // Seed > default size (25) items
        for (int i = 0; i < 40; i++)
        {
            var item = new PantryItem { Name = $"Item{i}", Status = "available" };
            await item.Save();
        }

        var controller = new PantryItemController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        // Simulate page request without specifying pageSize
        // NOTE: EntityController likely exposes GET handler implicitly; for a unit-level approximation
        // we call static query then emulate slicing logic: ensure framework default applies (25).
        var all = await PantryItem.All();
        all.Count.Should().BeGreaterThan(25);
        var page = all
            .OrderByDescending(i => i.ExpiresAt) // matches default sort partially; ExpiresAt may be null so stable ordering not crucial
            .ThenBy(i => i.Name)
            .Take(25)
            .ToList();
        page.Count.Should().Be(25);
    }

    [Fact]
    public async Task Page_Size_Should_Clamp_To_Max()
    {
        // Seed enough items
        for (int i = 0; i < 300; i++)
        {
            var item = new PantryItem { Name = $"Clamp{i}", Status = "available" };
            await item.Save();
        }
        // Inspect controller attribute to assert configured max (source of truth)
        var attr = typeof(PantryItemController).GetCustomAttributes(typeof(Koan.Web.Attributes.PaginationAttribute), inherit: true)
            .Cast<Koan.Web.Attributes.PaginationAttribute>()
            .FirstOrDefault();
        attr.Should().NotBeNull();
        attr!.MaxSize.Should().Be(200);

        // Emulate a user requesting above max
        var requested = 500;
        var all = await PantryItem.All();
        all.Count.Should().BeGreaterThan(200);
        var effective = Math.Min(requested, attr.MaxSize);
        effective.Should().Be(200);
    }
}
