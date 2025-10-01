using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Koan.Data.Core.Relationships;
using Koan.Web.Controllers;
using Xunit;
using FluentAssertions;

namespace Koan.Web.Controllers.Tests
{
    public class EntityControllerParentTests
    {
        public class TestParent { public string Id { get; set; } = "parent-1"; public string Name { get; set; } = "ParentName"; }
        public class TestEntity : Koan.Data.Core.Model.Entity<TestEntity> { public string Id { get; set; } = "entity-1"; [Parent(typeof(TestParent))] public string ParentId { get; set; } = "parent-1"; }

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
            protected override Koan.Data.Core.Model.Entity<TestEntity> GetEntity(string id) => _entity;
            protected override object GetParent(Type parentType, object parentId) => _parent;
            protected override Microsoft.AspNetCore.Http.HttpRequest HttpRequest => new TestHttpRequest(_queryKey, _queryValue);
        }
        public class TestHttpRequest : Microsoft.AspNetCore.Http.HttpRequest
        {
            private readonly string _key;
            private readonly string _value;
            public TestHttpRequest(string key, string value) { _key = key; _value = value; }
            public override Microsoft.AspNetCore.Http.IQueryCollection Query => new Microsoft.AspNetCore.Http.QueryCollection(new System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { { _key, _value } });
            public override Microsoft.AspNetCore.Http.HttpContext HttpContext => throw new System.NotImplementedException();
            public override string Method { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override string Scheme { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override bool IsHttps { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override Microsoft.AspNetCore.Http.HostString Host { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override Microsoft.AspNetCore.Http.PathString PathBase { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override Microsoft.AspNetCore.Http.PathString Path { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override Microsoft.AspNetCore.Http.QueryString QueryString { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override Microsoft.AspNetCore.Http.IHeaderDictionary Headers => throw new System.NotImplementedException();
            public override Microsoft.AspNetCore.Http.IRequestCookieCollection Cookies { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override long? ContentLength { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override string ContentType { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override System.IO.Stream Body { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override bool HasFormContentType => throw new System.NotImplementedException();
            public override Microsoft.AspNetCore.Http.IFormCollection Form { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override string Protocol { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
            public override System.IO.Pipelines.PipeReader BodyReader => throw new System.NotImplementedException();
            public override Task<Microsoft.AspNetCore.Http.IFormCollection> ReadFormAsync(System.Threading.CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
        }
    }
}
