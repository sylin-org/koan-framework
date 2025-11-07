#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"F:\Replica\NAS\Files\repo\github\koan-framework\src\Koan.Context\.koan\data\Koan.sqlite";
var connectionString = $"Data Source={dbPath}";

var connection = new SqliteConnection(connectionString);
connection.Open();

var command = connection.CreateCommand();
command.CommandText = @"
    SELECT name FROM sqlite_master
    WHERE type='table'
    ORDER BY name;
";

Console.WriteLine("Tables in Koan.sqlite:");
Console.WriteLine("======================");

var reader = command.ExecuteReader();
while (reader.Read())
{
    var tableName = reader.GetString(0);
    Console.WriteLine(tableName);
}

reader.Close();
connection.Close();
