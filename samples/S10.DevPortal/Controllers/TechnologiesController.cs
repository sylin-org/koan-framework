using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S10.DevPortal.Models;

namespace S10.DevPortal.Controllers;

/// <summary>
/// Demonstrates relationship navigation and hierarchical data patterns
/// </summary>
[Route("api/[controller]")]
public class TechnologiesController : EntityController<Technology>
{
    // Inherits all CRUD operations automatically

    /// <summary>
    /// Custom endpoint demonstrating relationship navigation
    /// </summary>
    [HttpGet("{id}/children")]
    public async Task<IActionResult> GetChildren(string id)
    {
        var tech = await Technology.Get(id);
        if (tech == null) return NotFound();

        // Self-referencing hierarchies require manual navigation
        var allTechs = await Technology.All();
        var children = allTechs.Where(t => t.ParentId == tech.Id).ToList();

        return Ok(children);
    }

    /// <summary>
    /// Demonstrates hierarchical relationship navigation
    /// </summary>
    [HttpGet("{id}/hierarchy")]
    public async Task<IActionResult> GetHierarchy(string id)
    {
        var tech = await Technology.Get(id);
        if (tech == null) return NotFound();

        // Self-referencing hierarchies require manual navigation
        var parent = !string.IsNullOrEmpty(tech.ParentId)
            ? await Technology.Get(tech.ParentId)
            : null;

        var allTechs = await Technology.All();
        var children = allTechs.Where(t => t.ParentId == tech.Id).ToList();

        var demo = new
        {
            Entity = tech,
            Parent = parent,
            Children = children,
            Related = tech.RelatedIds.Count > 0
                ? allTechs.Where(t => tech.RelatedIds.Contains(t.Id)).ToList()
                : new List<Technology>()
        };

        return Ok(demo);
    }

    /// <summary>
    /// Demonstrates soft relationships via RelatedIds
    /// </summary>
    [HttpGet("{id}/related")]
    public async Task<IActionResult> GetRelated(string id)
    {
        var tech = await Technology.Get(id);
        if (tech == null) return NotFound();

        var relatedTechs = new List<Technology>();
        foreach (var relatedId in tech.RelatedIds)
        {
            var related = await Technology.Get(relatedId);
            if (related != null) relatedTechs.Add(related);
        }

        return Ok(relatedTechs);
    }
}