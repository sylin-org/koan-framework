using Newtonsoft.Json.Linq;

namespace Koan.Web.Auth.Infrastructure;

internal static class UserInfoMapper
{
    // Maps roles, permissions, and extra claims from a UserInfo JObject
    public static (string[] Roles, string[] Permissions, List<KeyValuePair<string, string>> Extras) Map(JObject user)
    {
        if (user == null) return (Array.Empty<string>(), Array.Empty<string>(), new());

        var rolesArr = user["roles"] as JArray;
        var permsArr = user["permissions"] as JArray;
        var claimsObj = user["claims"] as JObject;

        var roles = rolesArr != null
            ? rolesArr.Select(t => t?.ToString() ?? string.Empty)
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToArray()
            : Array.Empty<string>();

        var perms = permsArr != null
            ? permsArr.Select(t => t?.ToString() ?? string.Empty)
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToArray()
            : Array.Empty<string>();

        var extras = new List<KeyValuePair<string, string>>();
        if (claimsObj != null)
        {
            foreach (var prop in claimsObj.Properties())
            {
                if (prop.Value is JArray arr)
                {
                    foreach (var v in arr)
                    {
                        var s = v?.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) extras.Add(new(prop.Name, s));
                    }
                }
                else
                {
                    var s = prop.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) extras.Add(new(prop.Name, s));
                }
            }
        }

        return (roles, perms, extras);
    }
}
