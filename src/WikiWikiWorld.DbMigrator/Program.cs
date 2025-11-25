using Microsoft.Data.Sqlite;

string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "WikiWikiWorld.db");
string sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "WikiWikiWorld.Data", "SqliteScripts", "MigrateToEfCore.sql");

Console.WriteLine($"Database Path: {dbPath}");
Console.WriteLine($"SQL Script Path: {sqlPath}");

if (!File.Exists(dbPath))
{
    Console.WriteLine("Database not found!");
    return;
}

if (!File.Exists(sqlPath))
{
    Console.WriteLine("SQL script not found!");
    return;
}

string sql = File.ReadAllText(sqlPath);

using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

Console.WriteLine("Migration completed successfully.");
