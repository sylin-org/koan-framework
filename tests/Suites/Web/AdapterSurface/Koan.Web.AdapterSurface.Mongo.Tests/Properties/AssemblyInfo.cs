using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.Mongo.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.Mongo.Tests.dll",
    priority: "0")]
