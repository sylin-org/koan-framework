using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.SqlServer.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.SqlServer.Tests.dll",
    priority: "0")]
