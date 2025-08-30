using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sora.Web.Auth.Tests;

public class UserInfoMappingTests
{
    [Fact]
    public void Maps_Roles_Permissions_And_Claims()
    {
        var json = @"{
  \"id\": \"jane@example.com\",
  \"username\": \"Jane\",
  \"email\": \"jane@example.com\",
  \"roles\": [\"admin\", \"author\"],
  \"permissions\": [\"content:write\", \"posts:delete\"],
  \"claims\": { \"department\": \"ENG\", \"scope\": [\"read\", \"write\"] }
}";
        var user = JObject.Parse(json);
        var rolesArr = user["roles"] as JArray;
        var permsArr = user["permissions"] as JArray;
        var extra = user["claims"] as JObject;

        var roles = rolesArr!.Select(t => t?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var perms = permsArr!.Select(t => t?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var extras = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>();
        foreach (var prop in extra!.Properties())
        {
            if (prop.Value is JArray arr)
                foreach (var v in arr) { var s = v?.ToString(); if (!string.IsNullOrWhiteSpace(s)) extras.Add(new(prop.Name, s)); }
            else { var s = prop.Value?.ToString(); if (!string.IsNullOrWhiteSpace(s)) extras.Add(new(prop.Name, s)); }
        }

        Assert.Contains("admin", roles);
        Assert.Contains("author", roles);
        Assert.Contains("content:write", perms);
        Assert.Contains("posts:delete", perms);
        Assert.Contains(extras, kv => kv.Key == "department" && kv.Value == "ENG");
        Assert.Equal(2, extras.Count(kv => kv.Key == "scope"));
    }
}