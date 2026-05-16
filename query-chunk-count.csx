#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"F:\Replica\NAS\Files\repo\github\koan-framework\src\Koan.Context\.koan\data\Koan.sqlite";
var connectionString = $"Data Source={dbPath}";

var connection = new SqliteConnection(connectionString);
connection.Open();

var command = connection.CreateCommand();
command.CommandText = @"
    SELECT COUNT(*)
    FROM ""Koan.Context.Models.DocumentChunk#proj-019a5c33623d75cea8a264fc2976f8c3"";
";

Console.WriteLine("Counting rows in DocumentChunk partition table...");
var count = command.ExecuteScalar();
Console.WriteLine($"Total chunks in partition: {count}");

connection.Close();
