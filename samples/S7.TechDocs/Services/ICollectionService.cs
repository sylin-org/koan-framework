using S7.TechDocs.Models;

namespace S7.TechDocs.Services;

public interface ICollectionService
{
    Task<IEnumerable<Collection>> GetAllAsync();
    Task<Collection?> GetByIdAsync(string id);
}