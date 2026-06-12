using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.Sqlite.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.Sqlite.Tests.dll",
    priority: "0")]
