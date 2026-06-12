using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.Couchbase.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.Couchbase.Tests.dll",
    priority: "0")]
