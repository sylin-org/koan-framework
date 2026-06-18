using Microsoft.AspNetCore.Mvc.Testing;

// Every spec in this assembly shares one process-global state surface: the in-memory data store keyed by
// "memory://sort-tests" plus the AppHost.Current singleton. xunit runs distinct test classes in parallel by
// default, so concurrent RemoveAll/seed across classes would race (and silently corrupt counts). Serialize
// the assembly — these tests are sub-second and the shared store makes isolation, not throughput, the priority.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

// WebApplicationFactory's content-root probe falls back to walking up the bin directory for a *.sln file.
// Our test outputs live under %TEMP%/Koan-framework (see tests/Directory.Build.props), so the probe fails.
// Anchoring the content root via this assembly attribute is the documented escape hatch.
[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.Sort.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.Sort.Tests.dll",
    priority: "0")]
