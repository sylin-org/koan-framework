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
    ""id"": ""jane@example.com"",
    ""username"": ""Jane"",
    ""email"": ""jane@example.com"",
    ""roles"": [""admin"", ""author""],
    ""permissions"": [""content:write"", ""posts:delete""],
    ""claims"": { ""department"": ""ENG"", ""scope"": [""read"", ""write""] }
}";
    var user = JObject.Parse(json);
    var (roles, perms, extras) = Sora.Web.Auth.Infrastructure.UserInfoMapper.Map(user);

        Assert.Contains("admin", roles);
        Assert.Contains("author", roles);
        Assert.Contains("content:write", perms);
        Assert.Contains("posts:delete", perms);
    Assert.Contains(extras, kv => kv.Key == "department" && kv.Value == "ENG");
    Assert.Equal(2, extras.Count(kv => kv.Key == "scope"));
    }
}