using Microsoft.AspNetCore.Mvc.Testing;

// WebApplicationFactory's content-root probe falls back to walking up the bin directory for a *.sln file.
// Our test outputs live under %TEMP%/Koan-framework (see tests/Directory.Build.props), so the probe fails.
// Anchoring the content root via this assembly attribute is the documented escape hatch.
[assembly: WebApplicationFactoryContentRoot(
    key: "Koan.Web.Sort.Tests",
    contentRootPath: ".",
    contentRootTest: "Koan.Web.Sort.Tests.dll",
    priority: "0")]
