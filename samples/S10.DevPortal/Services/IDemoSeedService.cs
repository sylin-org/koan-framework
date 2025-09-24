using S10.DevPortal.Models;

namespace S10.DevPortal.Services;

/// <summary>
/// Service for generating demo data to showcase framework capabilities
/// </summary>
public interface IDemoSeedService
{
    Task<object> SeedDemoData();
    Task<List<Article>> GenerateSampleArticles(int count);
    Task<List<Technology>> GenerateSampleTechnologies();
    Task<List<User>> GenerateSampleUsers();
    Task<List<Comment>> GenerateSampleComments(List<Article> articles, List<User> users);
}