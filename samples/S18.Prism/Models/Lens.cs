using Koan.Data.Core.Model;

namespace S18.Prism.Models;

public class Lens : Entity<Lens>
{
    public string Name { get; set; } = "";
    public string SpaceId { get; set; } = "";
    public string PromptName { get; set; } = "";
    public string[] FocusTopics { get; set; } = [];
}
