namespace S16.PantryPal;

public class IngestionOptions
{
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    // Default shelf life days by category (lowercased keys)
    public Dictionary<string,int> DefaultShelfLifeDaysByCategory { get; set; } = new()
    {
        ["produce"] = 5,
        ["dairy"] = 7,
        ["bakery"] = 3,
        ["meat"] = 3
    };
}
