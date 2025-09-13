using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Sora.Data.Core.Relationships;
using Sora.Web.Controllers;
using Xunit;
using FluentAssertions;

namespace Sora.Web.Controllers.Tests
{
    public class EntityControllerParentTests
    {
        public class TestParent { public string Id { get; set; } = "parent-1"; public string Name { get; set; } = "ParentName"; }
        public class TestEntity : Sora.Data.Core.Model.Entity<TestEntity> { public string Id { get; set; } = "entity-1"; [Parent(typeof(TestParent))] public string ParentId { get; set; } = "parent-1"; }

        [Fact]
        public async Task GetById_WithParentQuery_ReturnsParentInResponse()
        {
            // Arrange
            var controller = new TestEntityController();
            controller.SetupParent(new TestParent { Id = "parent-1", Name = "ParentName" });
            controller.SetupEntity(new TestEntity { Id = "entity-1", ParentId = "parent-1" });
            controller.SetQueryString("with", "ParentId");

            // Act
            var result = await controller.GetById("entity-1", CancellationToken.None);
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value as System.Collections.Generic.Dictionary<string, object>;

            // Assert
            response.Should().NotBeNull();
            response.Should().ContainKey("_parent");
            var parentDict = response["_parent"] as System.Collections.Generic.Dictionary<string, object>;
            parentDict.Should().NotBeNull();
            parentDict.Should().ContainKey("ParentId");
            var parent = parentDict["ParentId"] as TestParent;
            parent.Should().NotBeNull();
            parent.Id.Should().Be("parent-1");
            parent.Name.Should().Be("ParentName");
        }

        // Minimal test controller stub
        public class TestEntityController : EntityController<TestEntity, string>
        {
            private TestParent _parent;
            private TestEntity _entity;
            private string _queryKey;
            private string _queryValue;
            public void SetupParent(TestParent parent) => _parent = parent;
            public void SetupEntity(TestEntity entity) => _entity = entity;
            public void SetQueryString(string key, string value) { _queryKey = key; _queryValue = value; }
            protected override Sora.Data.Core.Model.Entity<TestEntity> GetEntity(string id) => _entity;
            protected override object GetParent(Type parentType, object parentId) => _parent;
            protected override Microsoft.AspNetCore.Http.HttpRequest HttpRequest => new TestHttpRequest(_queryKey, _queryValue);
        }
        public class TestHttpRequest : Microsoft.AspNetCore.Http.HttpRequest
        {
            private readonly string _key;
            private readonly string _value;
            public TestHttpRequest(string key, string value) { _key = key; _value = value; }
            public override Microsoft.AspNetCore.Http.IQueryCollection Query => new Microsoft.AspNetCore.Http.QueryCollection(new System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { { _key, _value } });
            // ...other members throw NotImplementedException
        }
    }
}
