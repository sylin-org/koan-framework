using Koan.Data.Core.Model;

namespace S18.Prism.Models;

public class Space : Entity<Space>
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public SpaceAccess Access { get; set; } = SpaceAccess.Private;
    public string[] MemberIds { get; set; } = [];

    // Per-space AI configuration
    public SpaceModels? Models { get; set; }
}

public enum SpaceAccess
{
    Private,
    Shared
}

public sealed record SpaceModels
{
    public string? ChatModel { get; init; }
    public string? EmbedModel { get; init; }
    public string? VisionModel { get; init; }
    public string? CodeModel { get; init; }
    public string? LoraAdapter { get; init; }
}
