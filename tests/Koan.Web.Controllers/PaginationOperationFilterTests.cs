using System.Reflection;
using FluentAssertions;
using Koan.Web.Attributes;
using Koan.Web.Connector.Swagger;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Moq;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace Koan.Web.Controllers;

public class PaginationOperationFilterTests
{
    private static OperationFilterContext CreateContext(MethodInfo method, string httpMethod = "GET")
    {
        var apiDescription = new ApiDescription
        {
            HttpMethod = httpMethod,
            ActionDescriptor = new ControllerActionDescriptor
            {
                MethodInfo = method,
                ControllerTypeInfo = method.DeclaringType!.GetTypeInfo()
            }
        };

        var schemaGenerator = new Mock<ISchemaGenerator>().Object;
        var schemaRepository = new SchemaRepository();
        return new OperationFilterContext(apiDescription, schemaGenerator, schemaRepository, method);
    }

    [Fact]
    public void AddsPagingParametersAndHeadersForOnMode()
    {
        var method = typeof(PaginatedController).GetMethod(nameof(PaginatedController.Get))!;
        var context = CreateContext(method);
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse()
            }
        };

        var filter = new PaginationOperationFilter();
        filter.Apply(operation, context);

        operation.Parameters.Should().Contain(p => p.Name == "page");
        operation.Parameters.Should().Contain(p => p.Name == "pageSize");
        operation.Responses.Should().ContainKey("413");
        operation.Responses["200"].Headers.Should().ContainKey("X-Page");
        operation.Responses["200"].Headers.Should().ContainKey("X-Total-Count");
    }

    [Fact]
    public void AddsAllParameterForOptionalMode()
    {
        var method = typeof(OptionalController).GetMethod(nameof(OptionalController.Get))!;
        var context = CreateContext(method);
        var operation = new OpenApiOperation { Responses = new OpenApiResponses { ["200"] = new OpenApiResponse() } };

        var filter = new PaginationOperationFilter();
        filter.Apply(operation, context);

        operation.Parameters.Should().Contain(p => p.Name == "all");
        operation.Responses["200"].Headers.Should().NotContainKey("X-Total-Count");
        operation.Responses.Should().ContainKey("413");
    }

    [Fact]
    public void SkipsNonGetOperations()
    {
        var method = typeof(PaginatedController).GetMethod(nameof(PaginatedController.Get))!;
        var context = CreateContext(method, httpMethod: "POST");
        var operation = new OpenApiOperation();

        var filter = new PaginationOperationFilter();
        filter.Apply(operation, context);

        operation.Parameters.Should().BeNull();
        operation.Responses.Should().BeNull();
    }

    [Pagination]
    private sealed class PaginatedController
    {
        public void Get()
        {
        }
    }

    [Pagination(Mode = PaginationMode.Optional, IncludeCount = false)]
    private sealed class OptionalController
    {
        public void Get()
        {
        }
    }
}

