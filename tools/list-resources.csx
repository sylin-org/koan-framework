#!/usr/bin/env dotnet-script

using System.Reflection;

if (Args.Count == 0)
{
    Console.WriteLine("Usage: dotnet script list-resources.csx <assembly-path>");
    return 1;
}

var assemblyPath = Args[0];
var assembly = Assembly.LoadFrom(assemblyPath);
var resources = assembly.GetManifestResourceNames();

Console.WriteLine($"Assembly: {assembly.FullName}");
Console.WriteLine($"Resource count: {resources.Length}");
Console.WriteLine();

foreach (var resource in resources.OrderBy(r => r))
{
    Console.WriteLine(resource);
}

return 0;
