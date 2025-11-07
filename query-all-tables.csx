#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"F:\Replica\NAS\Files\repo\github\koan-framework\src\Koan.Context\.koan\data\Koan.sqlite";
var connectionString = $"Data Source={dbPath}";

var connection = new SqliteConnection(connectionString);
connection.Open();

// Get all tables
var command = connection.CreateCommand();
command.CommandText = @"
    SELECT name FROM sqlite_master
    WHERE type='table'
    ORDER BY name;
";

Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine("SQLite Database Analysis");
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine();

var reader = command.ExecuteReader();
var tables = new List<string>();
while (reader.Read())
{
    tables.Add(reader.GetString(0));
}
reader.Close();

// Check row count for each table
foreach (var table in tables)
{
    var countCmd = connection.CreateCommand();
    countCmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
    var count = countCmd.ExecuteScalar();

    Console.WriteLine($"Table: {table}");
    Console.WriteLine($"  Rows: {count}");

    // Sample first row if exists
    if (Convert.ToInt64(count) > 0)
    {
        var sampleCmd = connection.CreateCommand();
        sampleCmd.CommandText = $"SELECT * FROM \"{table}\" LIMIT 1;";
        var sampleReader = sampleCmd.ExecuteReader();

        if (sampleReader.Read())
        {
            Console.WriteLine($"  Columns: {sampleReader.FieldCount}");
            Console.Write("  Sample ID: ");
            try
            {
                Console.WriteLine(sampleReader["Id"]);
            }
            catch
            {
                Console.WriteLine("(no Id column)");
            }
        }
        sampleReader.Close();
    }
    Console.WriteLine();
}

connection.Close();

Console.WriteLine("=".PadRight(80, '='));
