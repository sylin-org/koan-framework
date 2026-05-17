using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.Redis.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.Redis.Tests.dll",
    priority: "0")]
