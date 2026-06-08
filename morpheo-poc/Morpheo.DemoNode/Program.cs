using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Morpheo.Core;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Extensions;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Determine the port for the main client web UI/API (Kestrel port for users/curl)
var clientPortStr = Environment.GetEnvironmentVariable("CLIENT_PORT") ?? "80";
int.TryParse(clientPortStr, out var clientPort);
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(clientPort);
});

// Configure options & bootstrap config file if missing
var configPath = Path.Combine(Directory.GetCurrentDirectory(), "morpheo.runtime.json");
if (!File.Exists(configPath))
{
    var nodeName = Environment.GetEnvironmentVariable("NODE_NAME") ?? $"Node-{Guid.NewGuid().ToString()[..4]}";
    var mode = Environment.GetEnvironmentVariable("MODE") ?? "local-only";
    var centralServerUrl = Environment.GetEnvironmentVariable("CENTRAL_SERVER_URL");
    var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") ?? "Data Source=/app/data/morpheo.db";

    var runtimeConfig = new RuntimeConfig
    {
        NodeName = nodeName,
        HttpPort = 5000, // Morpheo discovery port inside container
        DatabaseType = "Sqlite",
        ConnectionString = connectionString,
        EnableMesh = mode.Equals("p2p-mesh", StringComparison.OrdinalIgnoreCase) || mode.Equals("hybrid", StringComparison.OrdinalIgnoreCase),
        CentralServerUrl = centralServerUrl
    };

    var manager = new MorpheoConfigManager();
    manager.Save(runtimeConfig);
    Console.WriteLine($"Bootstrapped config: NodeName={nodeName}, Mode={mode}, Port=5000, ConnectionString={connectionString}");
}

// Load config from the JSON file
var configManager = new MorpheoConfigManager();
var config = configManager.Load();

builder.Services.AddMorpheo(morpheo =>
{
    // Apply options from morpheo.runtime.json
    morpheo.LoadRuntimeConfiguration();

    // Map SignalR server/client behaviors dynamically
    if (config.NodeName.Equals("server", StringComparison.OrdinalIgnoreCase))
    {
        // Central Server runs the SignalR hub
        morpheo.AddSignalRServer();
        // Server should always run the server endpoints (Kestrel)
        morpheo.AddMesh();
    }
    else if (!string.IsNullOrEmpty(config.CentralServerUrl))
    {
        // Clients connect to the server's SignalR Hub
        var hubUrl = $"{config.CentralServerUrl.TrimEnd('/')}/morpheo/hub";
        morpheo.AddSignalRClient(hubUrl);
    }

    // Always enable the dashboard on Kestrel Port 5000 (/dashboard)
    morpheo.AddDashboard(options =>
    {
        options.Path = "/dashboard";
        options.Title = $"Morpheo - {config.NodeName}";
    });

    // Register Cluster Security if a secret is provided
    var clusterSecret = Environment.GetEnvironmentVariable("CLUSTER_SECRET");
    if (!string.IsNullOrEmpty(clusterSecret))
    {
        morpheo.AddClusterSecurity(clusterSecret);
    }
});

// Configure CORS so we can access from different hosts if needed
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

// Register the TodoItem entity with the synchronizer type resolver
var resolver = app.Services.GetRequiredService<SimpleTypeResolver>();
resolver.Register<TodoItem>();

// -------------------------
//  API ENDPOINTS (PORT CLIENT_PORT)
// -------------------------

app.MapGet("/", () => new
{
    message = $"Morpheo Node '{config.NodeName}' is active.",
    portClient = clientPort,
    portMorpheo = config.HttpPort,
    database = config.DatabaseType,
    endpoints = new[] { "GET /api/todos", "POST /api/todos", "DELETE /api/todos/{id}", "GET /api/status" },
    dashboardUrl = $"http://localhost:{config.HttpPort}/dashboard"
});

// GET /api/todos - Reconstructs state from Vector Clock / Sync Logs
app.MapGet("/api/todos", async (MorpheoDbContext db) =>
{
    var logs = await db.SyncLogs
        .Where(l => l.EntityName == "TodoItem")
        .ToListAsync();

    var todos = logs.GroupBy(l => l.EntityId)
        .Select(g =>
        {
            var latest = g.OrderByDescending(l => l.Timestamp).First();
            if (latest.Action == "DELETE") return null;
            try
            {
                return JsonSerializer.Deserialize<TodoItem>(latest.JsonData);
            }
            catch
            {
                return null;
            }
        })
        .Where(t => t != null)
        .ToList();

    return Results.Ok(todos);
});

// POST /api/todos - Creates or updates a Todo
app.MapPost("/api/todos", async ([FromBody] TodoItem todo, DataSyncService syncService) =>
{
    if (string.IsNullOrEmpty(todo.Id)) todo.Id = Guid.NewGuid().ToString();
    await syncService.BroadcastChangeAsync(todo, "UPDATE");
    return Results.Ok(todo);
});

// DELETE /api/todos/{id} - Deletes a Todo
app.MapDelete("/api/todos/{id}", async (string id, DataSyncService syncService) =>
{
    var todo = new TodoItem { Id = id };
    await syncService.BroadcastChangeAsync(todo, "DELETE");
    return Results.Ok(new { message = $"Todo {id} marked as DELETED" });
});

// GET /api/status - Returns configuration and status of the node
app.MapGet("/api/status", async (MorpheoOptions options, DataSyncService syncService, MorpheoDbContext db) =>
{
    var logCount = await db.SyncLogs.CountAsync();
    var logs = await db.SyncLogs.OrderByDescending(l => l.Timestamp).Take(10).ToListAsync();
    return Results.Ok(new
    {
        nodeName = options.NodeName,
        portMorpheo = options.DiscoveryPort,
        enableMesh = config.EnableMesh,
        centralServerUrl = config.CentralServerUrl,
        clusterSecretConfigured = !string.IsNullOrEmpty(options.ClusterSecret),
        logsCount = logCount,
        recentLogs = logs.Select(l => new { l.Id, l.EntityId, l.Action, l.Timestamp })
    });
});

await app.RunAsync();

public class TodoItem : MorpheoEntity
{
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
