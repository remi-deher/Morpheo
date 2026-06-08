using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morpheo.Core;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Extensions;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using Morpheo.Sdk.Blobs;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Port for client web UI and CRUD API (Kestrel port for users)
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
    Console.WriteLine($"Bootstrapped configuration for {nodeName}");
}

// Load config
var configManager = new MorpheoConfigManager();
var config = configManager.Load();

const string BlobsDirectory = "/app/data/blobs";

// Set up Morpheo Framework
builder.Services.AddMorpheo(morpheo =>
{
    morpheo.LoadRuntimeConfiguration();

    // Map SignalR server/client behaviors dynamically
    if (config.NodeName.Equals("server", StringComparison.OrdinalIgnoreCase))
    {
        morpheo.AddSignalRServer();
        morpheo.AddMesh();
    }
    else if (!string.IsNullOrEmpty(config.CentralServerUrl))
    {
        var hubUrl = $"{config.CentralServerUrl.TrimEnd('/')}/morpheo/hub";
        morpheo.AddSignalRClient(hubUrl);
    }

    // Enable local blob store in persistent volume
    morpheo.AddBlobStore(BlobsDirectory);

    // Register Dashboard on Port 5000 (/dashboard)
    morpheo.AddDashboard(options =>
    {
        options.Path = "/dashboard";
        options.Title = $"Morpheo - {config.NodeName}";
    });

    // Cluster Security
    var clusterSecret = Environment.GetEnvironmentVariable("CLUSTER_SECRET");
    if (!string.IsNullOrEmpty(clusterSecret))
    {
        morpheo.AddClusterSecurity(clusterSecret);
    }
});

// Register the custom EmulatedPrinterService to capture raw prints
builder.Services.Replace(ServiceDescriptor.Singleton<IPrintGateway, EmulatedPrinterService>());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

// Register the entity types in the simple type resolver
var resolver = app.Services.GetRequiredService<SimpleTypeResolver>();
resolver.Register<TodoItem>();
resolver.Register<TextNote>();
resolver.Register<SharedFile>();

// -------------------------
//  API ENDPOINTS (PORT CLIENT_PORT)
// -------------------------

// Serve the Single Page Application UI
app.MapGet("/", async () =>
{
    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
    if (!File.Exists(path))
    {
        path = Path.Combine(Directory.GetCurrentDirectory(), "index.html");
    }

    if (File.Exists(path))
    {
        var html = await File.ReadAllTextAsync(path);
        return Results.Content(html, "text/html");
    }
    return Results.NotFound("Dashboard index.html not found.");
});

// System Config API on port 80 for the Web UI
app.MapGet("/dashboard/api/sys/config", (MorpheoConfigManager manager) =>
{
    return Results.Ok(manager.Load());
});

app.MapPost("/dashboard/api/sys/config", ([FromBody] RuntimeConfig config, MorpheoConfigManager manager) =>
{
    manager.Save(config);
    return Results.Ok();
});

app.MapPost("/dashboard/api/sys/restart", (Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime) =>
{
    lifetime.StopApplication();
    return Results.Ok("Stopping application...");
});

// Notes CRUD
app.MapGet("/api/notes", async (MorpheoDbContext db) =>
{
    var logs = await db.SyncLogs
        .Where(l => l.EntityName == "TextNote")
        .ToListAsync();

    var notes = logs.GroupBy(l => l.EntityId)
        .Select(g =>
        {
            var latest = g.OrderByDescending(l => l.Timestamp).First();
            if (latest.Action == "DELETE") return null;
            try
            {
                return JsonSerializer.Deserialize<TextNote>(latest.JsonData);
            }
            catch
            {
                return null;
            }
        })
        .Where(n => n != null)
        .ToList();

    return Results.Ok(notes);
});

app.MapPost("/api/notes", async ([FromBody] TextNote note, DataSyncService syncService) =>
{
    if (string.IsNullOrEmpty(note.Id)) note.Id = Guid.NewGuid().ToString();
    await syncService.BroadcastChangeAsync(note, "UPDATE");
    return Results.Ok(note);
});

app.MapDelete("/api/notes/{id}", async (string id, DataSyncService syncService) =>
{
    var note = new TextNote { Id = id };
    await syncService.BroadcastChangeAsync(note, "DELETE");
    return Results.Ok(new { message = $"Note {id} deleted" });
});

// File Exchanger
app.MapGet("/api/files", async (MorpheoDbContext db) =>
{
    var logs = await db.SyncLogs
        .Where(l => l.EntityName == "SharedFile")
        .ToListAsync();

    var files = logs.GroupBy(l => l.EntityId)
        .Select(g =>
        {
            var latest = g.OrderByDescending(l => l.Timestamp).First();
            if (latest.Action == "DELETE") return null;
            try
            {
                return JsonSerializer.Deserialize<SharedFile>(latest.JsonData);
            }
            catch
            {
                return null;
            }
        })
        .Where(f => f != null)
        .ToList();

    return Results.Ok(files);
});

app.MapPost("/api/files/upload", async (HttpRequest request, IMorpheoBlobStore blobStore, DataSyncService syncService, MorpheoOptions options) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("Invalid form content.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var uploader = form["uploader"].ToString();

    if (file == null || file.Length == 0) return Results.BadRequest("No file uploaded.");

    using var stream = file.OpenReadStream();
    
    // Save locally to blob store
    var blobId = await blobStore.SaveBlobAsync(stream, file.FileName, file.ContentType);

    // Broadcast file metadata to peers
    var sharedFile = new SharedFile
    {
        Id = Guid.NewGuid().ToString(),
        BlobId = blobId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        SizeBytes = file.Length,
        Uploader = string.IsNullOrEmpty(uploader) ? options.NodeName : uploader,
        UploadedAt = DateTime.UtcNow
    };

    await syncService.BroadcastChangeAsync(sharedFile, "UPDATE");

    return Results.Ok(sharedFile);
});

app.MapGet("/api/files/download/{blobId}", async (string blobId, IMorpheoBlobStore blobStore, MorpheoDbContext db, INetworkDiscovery discovery, MorpheoOptions options, IHttpClientFactory httpClientFactory) =>
{
    // Try local download first
    var stream = await blobStore.GetBlobStreamAsync(blobId);
    var metadata = await blobStore.GetBlobMetadataAsync(blobId);

    if (stream == null || metadata == null)
    {
        // Not present locally. Retrieve metadata from database to know original name & content type
        var fileLog = await db.SyncLogs
            .Where(l => l.EntityName == "SharedFile" && l.JsonData.Contains(blobId))
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync();

        SharedFile? fileInfo = null;
        if (fileLog != null && fileLog.Action != "DELETE")
        {
            try
            {
                fileInfo = JsonSerializer.Deserialize<SharedFile>(fileLog.JsonData);
            }
            catch {}
        }

        if (fileInfo == null) return Results.NotFound("File metadata not found in cluster.");

        // Pull the binary data from one of our discovered network peers (P2P download)
        var peers = discovery.GetPeers();
        bool downloaded = false;
        var scheme = options.UseSecureConnection ? "https" : "http";

        foreach (var peer in peers)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var url = $"{scheme}://{peer.IpAddress}:{peer.Port}/morpheo/blobs/{blobId}";

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    using var remoteStream = await response.Content.ReadAsStreamAsync();
                    await CacheRemoteBlobAsync(BlobsDirectory, blobId, remoteStream, fileInfo.FileName, fileInfo.ContentType, fileInfo.SizeBytes);
                    downloaded = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to pull blob {blobId} from peer {peer.Name}: {ex.Message}");
            }
        }

        if (!downloaded) return Results.NotFound("File not found on any active peer.");

        // Read again now that it is saved locally
        stream = await blobStore.GetBlobStreamAsync(blobId);
        metadata = await blobStore.GetBlobMetadataAsync(blobId);
    }

    if (stream == null || metadata == null) return Results.NotFound("File missing after download.");

    return Results.File(stream, metadata.ContentType, metadata.FileName);
});

// Network Print Emulator
app.MapPost("/api/print", async ([FromBody] PrintRequest request, IPrintGateway printGateway) =>
{
    // Network prints are received here (triggered by SendPrintJobAsync from another peer)
    var printer = "Virtual-Zebra-01"; // Default virtual printer for network jobs
    var contentBytes = System.Text.Encoding.UTF8.GetBytes(request.Content);
    await printGateway.PrintAsync(printer, contentBytes);

    EmulatedPrinter.PrintJobs.Add(new PrintJobRecord
    {
        Id = Guid.NewGuid().ToString(),
        PrinterName = printer,
        Content = request.Content,
        Sender = request.Sender,
        Timestamp = DateTime.UtcNow
    });

    return Results.Ok();
});

app.MapGet("/api/print/jobs", () => Results.Ok(EmulatedPrinter.PrintJobs));

app.MapPost("/api/print/send", async ([FromBody] SendPrintJobRequest req, IMorpheoClient client) =>
{
    // Sends a print job to a target node
    var target = new PeerInfo(req.PeerId, req.PeerName, req.PeerIp, req.PeerPort, NodeRole.StandardClient, Array.Empty<string>());
    await client.SendPrintJobAsync(target, req.Content);
    return Results.Ok(new { message = $"Print job sent to {req.PeerName}" });
});

// Peers list
app.MapGet("/api/peers", (INetworkDiscovery discovery) =>
{
    var peers = discovery.GetPeers();
    return Results.Ok(peers);
});

app.MapGet("/api/status", async (MorpheoOptions options, DataSyncService syncService, MorpheoDbContext db) =>
{
    var logCount = await db.SyncLogs.CountAsync();
    return Results.Ok(new
    {
        nodeName = options.NodeName,
        mode = config.EnableMesh ? "hybrid / mesh" : "client-server",
        portMorpheo = options.DiscoveryPort,
        clusterSecretConfigured = !string.IsNullOrEmpty(options.ClusterSecret),
        logsCount = logCount
    });
});

await app.RunAsync();

// Helper method to write remote blob directly into local storage
static async Task CacheRemoteBlobAsync(string blobsDir, string blobId, Stream contentStream, string fileName, string contentType, long sizeBytes)
{
    var filePath = Path.Combine(blobsDir, blobId);
    var metaPath = Path.Combine(blobsDir, blobId + ".meta.json");

    Directory.CreateDirectory(blobsDir);

    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
    {
        await contentStream.CopyToAsync(fileStream);
    }

    var metadata = new BlobMetadata
    {
        BlobId = blobId,
        FileName = fileName,
        ContentType = contentType,
        SizeBytes = sizeBytes,
        Hash = string.Empty
    };

    var json = JsonSerializer.Serialize(metadata);
    await File.WriteAllTextAsync(metaPath, json);
}

// -------------------------
//  MODELS & SERVICES
// -------------------------

public class TodoItem : MorpheoEntity
{
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

public class TextNote : MorpheoEntity
{
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SharedFile : MorpheoEntity
{
    public string BlobId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Uploader { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class PrintRequest
{
    public string Content { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
}

public class SendPrintJobRequest
{
    public string PeerId { get; set; } = string.Empty;
    public string PeerName { get; set; } = string.Empty;
    public string PeerIp { get; set; } = string.Empty;
    public int PeerPort { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public static class EmulatedPrinter
{
    public static List<PrintJobRecord> PrintJobs { get; } = new();
}

public class PrintJobRecord
{
    public string Id { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class EmulatedPrinterService : IPrintGateway
{
    private readonly ILogger<EmulatedPrinterService> _logger;

    public EmulatedPrinterService(ILogger<EmulatedPrinterService> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        return Task.FromResult<IEnumerable<string>>(new[] { "Virtual-Zebra-01", "Virtual-Receipt-02" });
    }

    public Task PrintAsync(string printerName, byte[] content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        _logger.LogInformation($"[PRINTER EMULATOR] Printing to '{printerName}' ({content.Length} bytes): {text}");
        return Task.CompletedTask;
    }
}
