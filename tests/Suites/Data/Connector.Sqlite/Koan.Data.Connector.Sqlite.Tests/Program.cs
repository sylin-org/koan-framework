using Koan.Data.Connector.Sqlite.Tests.Specs;

// The test assembly doubles as the second process of SqliteHeldTransactionSpec: the same entities and the same connector,
// contending for one database file from a real other process. Every other invocation is the xUnit runner.
return args is [HeldContender.Verb, ..]
    ? await HeldContender.Run(args[1..])
    : await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args);
