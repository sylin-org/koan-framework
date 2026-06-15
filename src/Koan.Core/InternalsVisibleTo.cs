using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Koan.Core.Tests")]
[assembly: InternalsVisibleTo("Koan.Tests.Core.Unit")]
[assembly: InternalsVisibleTo("Koan.Tests.Integration.Bootstrap")]
[assembly: InternalsVisibleTo("Koan.Tests.Data.Core")] // KoanLog.TestSink seam — X-f2-failure-coverage ConfigWarning capture
