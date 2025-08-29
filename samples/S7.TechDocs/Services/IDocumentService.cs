using S7.TechDocs.Models;

namespace S7.TechDocs.Services;

public interface IDocumentService
{
    Task<IEnumerable<Document>> GetAllAsync();
    Task<IEnumerable<Document>> GetByCollectionAsync(string collectionId);
    Task<IEnumerable<Document>> GetByStatusAsync(string status);
    Task<Document?> GetByIdAsync(string id);
    Task<Document> CreateAsync(Document document);
    Task<Document> UpdateAsync(Document document);
    Task DeleteAsync(string id);
}