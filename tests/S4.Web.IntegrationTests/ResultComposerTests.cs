using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Koan.Web.GraphQl.Infrastructure;
using System.Collections.Generic;
using Xunit;

namespace S4.Web.IntegrationTests;

public class ResultComposerTests
{
    [Fact]
    public void Compose_should_include_data_and_errors()
    {
        var qb = QueryResultBuilder.New();
        qb.SetData(new Dictionary<string, object?> { ["a"] = 1 });
        qb.AddError(ErrorBuilder.New().SetMessage("e").SetCode("X").Build());
        qb.SetExtension("x", 1);
        var qr = qb.Create();
        var http = new DefaultHttpContext();
        var payload = ResultComposer.Compose(qr, http, null, null) as IDictionary<string, object?>;
        payload!.ContainsKey("data").Should().BeTrue();
        payload.ContainsKey("errors").Should().BeTrue();
        payload.ContainsKey("extensions").Should().BeTrue();
    }
}
