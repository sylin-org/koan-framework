using Koan.ZenGarden;
using Koan.ZenGarden.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Zen Garden + Koan Framework Integration Test         ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<ZenGardenClient>();

// Create the client
using var httpClient = new HttpClient();
using var zenClient = new ZenGardenClient(httpClient, logger, new ZenGardenOptions
{
    DiscoveryTimeoutSeconds = 5,
    HttpTimeoutSeconds = 10
});

Console.WriteLine("🔍 Step 1: Discovering Stones on the network...");
Console.WriteLine();

var stones = await zenClient.DiscoverStonesAsync();

if (stones.Count == 0)
{
    Console.WriteLine("❌ No Stones found on the network!");
    Console.WriteLine("   Make sure you have a Zen Garden Stone running.");
    return 1;
}

Console.WriteLine($"✅ Found {stones.Count} Stone(s):");
foreach (var stone in stones)
{
    Console.WriteLine($"   📦 {stone.StoneName}");
    Console.WriteLine($"      Endpoint: {stone.StoneEndpoint}");
    Console.WriteLine($"      Version:  {stone.MossVersion ?? "unknown"}");
}
Console.WriteLine();

Console.WriteLine("🔍 Step 2: Finding MongoDB service...");
Console.WriteLine();

var mongoService = await zenClient.FindServiceAsync("mongodb");

if (mongoService == null)
{
    Console.WriteLine("❌ MongoDB not found in the Garden!");
    Console.WriteLine("   Make sure MongoDB is installed on one of your Stones.");
    
    // List available services
    Console.WriteLine();
    Console.WriteLine("Available services:");
    foreach (var stone in stones)
    {
        var services = await zenClient.GetServicesAsync(stone);
        foreach (var svc in services)
        {
            Console.WriteLine($"   • {svc.Offering} ({svc.Status}) on {stone.StoneName}");
        }
    }
    return 1;
}

Console.WriteLine($"✅ Found MongoDB!");
Console.WriteLine($"   Stone:            {mongoService.Stone.StoneName}");
Console.WriteLine($"   Status:           {mongoService.Service.Status}");
Console.WriteLine($"   Health:           {mongoService.Service.Health}");
Console.WriteLine($"   Connection String: {mongoService.ConnectionString}");
Console.WriteLine();

Console.WriteLine("🔌 Step 3: Connecting to MongoDB...");
Console.WriteLine();

try
{
    var mongoClient = new MongoClient(mongoService.ConnectionString);
    
    // Test the connection
    var databases = await mongoClient.ListDatabaseNamesAsync();
    var dbList = await databases.ToListAsync();
    
    Console.WriteLine($"✅ Connected! Found {dbList.Count} database(s):");
    foreach (var db in dbList)
    {
        Console.WriteLine($"   📁 {db}");
    }
    Console.WriteLine();
    
    Console.WriteLine("📝 Step 4: Writing a test record...");
    Console.WriteLine();
    
    var database = mongoClient.GetDatabase("koan_test");
    var collection = database.GetCollection<BsonDocument>("zen_garden_discovery_test");
    
    var document = new BsonDocument
    {
        { "test", "Zen Garden Discovery" },
        { "framework", "Koan" },
        { "stone", mongoService.Stone.StoneName },
        { "timestamp", DateTime.UtcNow },
        { "message", "Hello from Koan.ZenGarden! 🌱" }
    };
    
    await collection.InsertOneAsync(document);
    Console.WriteLine($"✅ Inserted document with _id: {document["_id"]}");
    Console.WriteLine();
    
    // Read it back
    var filter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);
    var found = await collection.Find(filter).FirstOrDefaultAsync();
    
    if (found != null)
    {
        Console.WriteLine("📖 Read back the document:");
        Console.WriteLine($"   {found.ToJson()}");
    }
    Console.WriteLine();
    
    // Clean up
    await collection.DeleteOneAsync(filter);
    Console.WriteLine("🗑️  Cleaned up test document.");
    Console.WriteLine();
    
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("✅ SUCCESS! Zen Garden integration is working!");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine();
    Console.WriteLine("Summary:");
    Console.WriteLine($"  • Discovered {stones.Count} Stone(s) via UDP multicast");
    Console.WriteLine($"  • Found MongoDB on {mongoService.Stone.StoneName}");
    Console.WriteLine($"  • Connected using: {mongoService.ConnectionString}");
    Console.WriteLine($"  • Successfully wrote and read a test record");
    Console.WriteLine();
    
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"❌ MongoDB connection failed: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Debug info:");
    Console.WriteLine($"  Connection string: {mongoService.ConnectionString}");
    Console.WriteLine($"  Stone endpoint: {mongoService.Stone.StoneEndpoint}");
    Console.WriteLine();
    Console.WriteLine("Possible fixes:");
    Console.WriteLine("  1. Check if MongoDB container is running on the Stone");
    Console.WriteLine("  2. Verify the connection string scheme (should be mongodb://)");
    Console.WriteLine("  3. Check firewall/network connectivity to the Stone");
    return 1;
}
