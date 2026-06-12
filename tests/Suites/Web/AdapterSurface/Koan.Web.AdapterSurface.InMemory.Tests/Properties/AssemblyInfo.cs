using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// The three per-adapter spec classes (Surface, Partition, Transfer) share an AppHost.Current
// static, so running them in parallel races on global state. Serialize at the assembly level.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.AdapterSurface.InMemory.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.AdapterSurface.InMemory.Tests.dll",
    priority: "0")]
