using S7.TechDocs.Models;

namespace S7.TechDocs.Services;

public interface IUserService
{
    Task<IEnumerable<User>> GetAllAsync();
    Task<User?> GetByIdAsync(string id);
    Task<User> UpdateRolesAsync(string id, List<string> roles);
}