using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run <assembly-path>");
            return;
        }

        var assemblyPath = args[0];
        var assembly = Assembly.LoadFrom(assemblyPath);
        var resources = assembly.GetManifestResourceNames();

        Console.WriteLine($"Assembly: {assembly.FullName}");
        Console.WriteLine($"Resource count: {resources.Length}");
        Console.WriteLine();

        foreach (var resource in resources.OrderBy(r => r))
        {
            Console.WriteLine(resource);
        }
    }
}
