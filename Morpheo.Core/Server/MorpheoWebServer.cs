using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting; // Added for ConfigureKestrel
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Morpheo.Sdk;
using Morpheo.Core.Sync;
using Morpheo.Core.Security;
using Morpheo.Usb;
using Morpheo.Usb.Audit;

namespace Morpheo.Core.Server;

/// <summary>
/// Embedded Web Server (Kestrel) hosting the Morpheo API.
/// </summary>
public class MorpheoWebServer : IMorpheoServer
{
    private WebApplication? _app;
    private readonly MorpheoOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public int LocalPort { get; private set; }

    public MorpheoWebServer(MorpheoOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();

        // Kestrel Configuration
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ListenAnyIP(_options.DiscoveryPort);
        });

        // Register parent services to the new service provider
        // This ensures we reuse instances from the parent DI container
        builder.Services.AddSingleton(sp => _serviceProvider.GetRequiredService<DataSyncService>());
        // IRequestAuthenticator is optional — tests that build their own ServiceProvider may omit it.
        var auth = _serviceProvider.GetService<IRequestAuthenticator>();
        if (auth != null)
            builder.Services.AddSingleton(auth);
        // IMorpheoBlobStore is optional — only registered if AddBlobStore() was called
        var blobStore = _serviceProvider.GetService<Morpheo.Sdk.Blobs.IMorpheoBlobStore>();
        if (blobStore != null)
            builder.Services.AddSingleton(blobStore);
        builder.Services.AddSingleton(sp => _options);

        // IPrintGateway is optional/standard
        var printGateway = _serviceProvider.GetService<Morpheo.Sdk.IPrintGateway>();
        if (printGateway != null)
            builder.Services.AddSingleton<Morpheo.Sdk.IPrintGateway>(printGateway);

        // USB subsystem services — bridge from parent container if registered
        var usbEnumerator = _serviceProvider.GetService<IUsbDeviceEnumerator>();
        if (usbEnumerator != null)
        {
            builder.Services.AddSingleton(usbEnumerator);
            var usbDriver = _serviceProvider.GetService<IUsbDriver>();
            var usbPolicy = _serviceProvider.GetService<IUsbAccessPolicy>();
            var usbAudit = _serviceProvider.GetService<IUsbAuditLogger>();
            if (usbDriver != null) builder.Services.AddSingleton(usbDriver);
            if (usbPolicy != null) builder.Services.AddSingleton(usbPolicy);
            if (usbAudit != null) builder.Services.AddSingleton(usbAudit);
        }

        builder.Services.AddSignalR();
        builder.Services.AddAuthorization();

        // Health probes for container/daemon orchestration.
        //  - "live": process is up and serving (no dependency check).
        //  - "ready": the node can actually serve — database reachable.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

        // Transparently decompress gzip/deflate request bodies (large batch pushes)
        // and compress responses (large Cold Sync history pulls).
        builder.Services.AddRequestDecompression();
        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
                .Concat(new[] { "application/json" });
        });

        _app = builder.Build();

        // Register the active HubContext to bridge parent and child DI containers
        Morpheo.Core.Sync.Strategies.SignalRHubContextLocator.HubContext =
            _app.Services.GetService<Microsoft.AspNetCore.SignalR.IHubContext<Morpheo.Core.SignalR.MorpheoSyncHub>>();

        // Decompression must run BEFORE authentication so the HMAC is verified against
        // the decompressed body — the same bytes the client signed.
        _app.UseRequestDecompression();
        _app.UseResponseCompression();

        // Morpheo Authentication Middleware
        _app.Use(async (context, next) =>
        {
            // Health probes and SignalR Hub must stay reachable without a signature
            // (SignalR Hub upgrade requests cannot be signed dynamically due to dynamic query parameters).
            if (context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/morpheo/hub"))
            {
                await next();
                return;
            }

            var auth = context.RequestServices.GetService<IRequestAuthenticator>();
            if (auth != null)
            {
                if (!await auth.IsAuthorizedAsync(context))
                {
                    context.Response.StatusCode = 401;
                    return;
                }
            }
            await next();
        });

        // Endpoint for receiving logs (Sync Push)
        // NOTE: Using both /api/sync and /morpheo/sync/push for backward compatibility
        _app.MapPost("/api/sync", async ([FromBody] SyncLogDto dto, [FromServices] DataSyncService syncService) =>
        {
            if (dto == null) return Results.BadRequest();
            await syncService.ReceiveRemoteLogAsync(dto);
            return Results.Ok();
        });

        _app.MapPost("/morpheo/sync/push", async ([FromBody] SyncLogDto dto, [FromServices] DataSyncService syncService) =>
        {
            if (dto == null) return Results.BadRequest();
            await syncService.ReceiveRemoteLogAsync(dto);
            return Results.Ok();
        });

        // Endpoint for receiving multiple logs in one request (batched push)
        _app.MapPost("/api/sync/batch", async ([FromBody] List<SyncLogDto> dtos, [FromServices] DataSyncService syncService) =>
        {
            if (dtos == null || dtos.Count == 0) return Results.BadRequest();
            await syncService.ReceiveRemoteBatchAsync(dtos);
            return Results.Ok();
        });

        // Endpoint for Cold Sync history pull
        _app.MapGet("/api/sync/history", async ([FromQuery] long since, [FromQuery] int? limit, [FromServices] DataSyncService syncService) =>
        {
            var logs = await syncService.GetHistoryAsync(since, limit ?? DataSyncService.DefaultHistoryPageSize);
            return Results.Ok(logs);
        });

        // --- Anti-entropy (Merkle reconciliation) endpoints ---

        // Merkle root digest over the local set of log Ids.
        _app.MapGet("/api/sync/digest", async ([FromServices] DataSyncService syncService) =>
        {
            var digest = await syncService.ComputeLocalDigestAsync();
            return Results.Ok(new { digest });
        });

        // Full manifest of locally held log Ids.
        _app.MapGet("/api/sync/manifest", async ([FromServices] DataSyncService syncService) =>
        {
            var ids = await syncService.GetManifestAsync();
            return Results.Ok(ids);
        });

        // Pull specific logs by Id (the events a peer has found it is missing).
        _app.MapPost("/api/sync/by-ids", async ([FromBody] List<string> ids, [FromServices] DataSyncService syncService) =>
        {
            if (ids == null || ids.Count == 0) return Results.BadRequest();
            var logs = await syncService.GetLogsByIdsAsync(ids);
            return Results.Ok(logs);
        });

        // Endpoint for receiving print jobs from remote peers
        _app.MapPost("/api/print", async ([FromBody] PrintRequestDto request, [FromServices] IPrintGateway printGateway) =>
        {
            if (request == null || string.IsNullOrEmpty(request.Content)) return Results.BadRequest("Invalid print request.");
            var printer = string.IsNullOrEmpty(request.PrinterName) ? "Virtual-Zebra-01" : request.PrinterName;
            var payload = JsonSerializer.Serialize(request);
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(payload);
            await printGateway.PrintAsync(printer, contentBytes);
            return Results.Ok();
        });

        // ─────────────────────────────────────────────────────────────
        //  USB DEVICE API  (only active when AddMorpheoUsb() is called)
        // ─────────────────────────────────────────────────────────────

        // List all currently connected USB devices visible to this node
        _app.MapGet("/api/usb/devices", async (HttpContext ctx) =>
        {
            var enumerator = ctx.RequestServices.GetService<IUsbDeviceEnumerator>();
            if (enumerator == null) return Results.NotFound("USB subsystem not enabled.");
            var principal = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            var policy = ctx.RequestServices.GetService<IUsbAccessPolicy>();
            var devices = await enumerator.EnumerateAsync(ctx.RequestAborted);
            var visible = new List<object>();
            foreach (var d in devices)
            {
                bool ok = policy == null || await policy.CanAccessDeviceAsync(principal, d, UsbAccessType.Read, ctx.RequestAborted);
                if (ok) visible.Add(new { d.Id, d.Label, d.VendorId, d.ProductId, d.Manufacturer, d.ProductName, d.DeviceClass, d.CapacityBytes, d.IsReadOnly, d.SerialNumber, d.IsMock, d.DiscoveredAt });
            }
            return Results.Ok(visible);
        });

        // List files on a USB storage device
        _app.MapGet("/api/usb/storage/{deviceId}/files", async (string deviceId, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            var files = await driver.ListFilesAsync(deviceId, ctx.RequestAborted);
            return Results.Ok(files);
        });

        // Read / download a file from a USB storage device
        _app.MapGet("/api/usb/storage/{deviceId}/files/{**filePath}", async (string deviceId, string filePath, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            var stream = await driver.ReadFileAsync(deviceId, "/" + filePath, ctx.RequestAborted);
            if (stream == null) return Results.NotFound("File not found on device.");
            var ct = filePath.EndsWith(".pdf") ? "application/pdf" : "application/octet-stream";
            return Results.File(stream, ct, Path.GetFileName(filePath));
        });

        // Upload a file to a USB storage device
        _app.MapPost("/api/usb/storage/{deviceId}/files", async (string deviceId, HttpRequest req, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            if (!req.HasFormContentType) return Results.BadRequest("Multipart form expected.");
            var form = await req.ReadFormAsync(ctx.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file == null) return Results.BadRequest("No file in form.");
            using var stream = file.OpenReadStream();
            var path = "/" + file.FileName.Replace("..", "__");
            var written = await driver.WriteFileAsync(deviceId, path, stream, file.ContentType, ctx.RequestAborted);
            return Results.Ok(new { path, written });
        });

        // Delete a file from a USB storage device
        _app.MapDelete("/api/usb/storage/{deviceId}/files/{**filePath}", async (string deviceId, string filePath, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            await driver.DeleteFileAsync(deviceId, "/" + filePath, ctx.RequestAborted);
            return Results.Ok();
        });

        // List USB printers
        _app.MapGet("/api/usb/printers", async (HttpContext ctx) =>
        {
            var enumerator = ctx.RequestServices.GetService<IUsbDeviceEnumerator>();
            if (enumerator == null) return Results.NotFound("USB subsystem not enabled.");
            var printers = await enumerator.EnumerateByClassAsync(UsbDeviceClass.Printer, ctx.RequestAborted);
            return Results.Ok(printers.Select(p => new { p.Id, p.Label, p.Manufacturer, p.ProductName, p.SerialNumber, p.IsMock }));
        });

        // Send RAW print job to a USB printer
        _app.MapPost("/api/usb/print/{deviceId}", async (string deviceId, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
            await driver.SendPrintJobAsync(deviceId, ms.ToArray(), ctx.RequestAborted);
            return Results.Accepted();
        });

        // Get printer status
        _app.MapGet("/api/usb/print/{deviceId}/status", async (string deviceId, HttpContext ctx) =>
        {
            var driver = ctx.RequestServices.GetService<IUsbDriver>();
            if (driver == null) return Results.NotFound("USB subsystem not enabled.");
            var status = await driver.GetPrinterStatusAsync(deviceId, ctx.RequestAborted);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        // USB Audit log query
        _app.MapGet("/api/usb/audit", async ([FromQuery] string? principalId, [FromQuery] string? deviceId, [FromQuery] int limit, HttpContext ctx) =>
        {
            var audit = ctx.RequestServices.GetService<IUsbAuditLogger>();
            if (audit == null) return Results.NotFound("USB audit not enabled.");
            var events = await audit.QueryAsync(principalId, deviceId, limit: limit == 0 ? 100 : limit);
            return Results.Ok(events);
        });

        // Health probe endpoints (unauthenticated, see middleware above).
        _app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });
        _app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        // Endpoint SignalR
        _app.MapHub<Morpheo.Core.SignalR.MorpheoSyncHub>("/morpheo/hub");

        // Endpoint for serving Blobs (only active if AddBlobStore() was called)
        _app.MapGet("/morpheo/blobs/{id}", async (string id, HttpContext httpContext) =>
        {
            var blobStore = httpContext.RequestServices.GetService<Morpheo.Sdk.Blobs.IMorpheoBlobStore>();
            if (blobStore == null) return Results.NotFound("Blob store not configured.");

            var stream = await blobStore.GetBlobStreamAsync(id);
            if (stream == null) return Results.NotFound();

            var metadata = await blobStore.GetBlobMetadataAsync(id);
            var contentType = metadata?.ContentType ?? "application/octet-stream";

            return Results.File(stream, contentType);
        });

        // -------------------------
        //     DASHBOARD
        // -------------------------
        var dashboardOptions = _serviceProvider.GetService<DashboardOptions>();
        if (dashboardOptions != null)
        {
            // Endpoint API Stats
            _app.MapGet(dashboardOptions.Path + "/api/stats", ([FromServices] DataSyncService sync) =>
            {
                return Results.Ok(new
                {
                    nodeName = _options.NodeName,
                    port = _options.DiscoveryPort,
                    dbType = ResolveDatabaseProvider()
                });
            });

            // Endpoint HTML
            _app.MapGet(dashboardOptions.Path, async (HttpContext context) =>
            {
                var assembly = typeof(MorpheoWebServer).Assembly;
                var resourceName = "Morpheo.Core.UI.index.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return Results.NotFound("Dashboard UI not found in embedded resources.");
                }

                using var reader = new StreamReader(stream);
                var html = await reader.ReadToEndAsync();

                // Variable injection
                html = html.Replace("{{TITLE}}", dashboardOptions.Title)
                           .Replace("{{PATH}}", dashboardOptions.Path);

                return Results.Content(html, "text/html");
            });

            // -------------------------
            //     SYSTEM CONFIG API
            // -------------------------
            // GET Config
            _app.MapGet(dashboardOptions.Path + "/api/sys/config", ([FromServices] IServiceProvider sp) =>
            {
                var manager = sp.GetService<Morpheo.Core.Configuration.MorpheoConfigManager>();
                if (manager == null) return Results.NotFound("Config Manager not active.");
                return Results.Ok(manager.Load());
            });

            // POST Config
            _app.MapPost(dashboardOptions.Path + "/api/sys/config", ([FromBody] RuntimeConfig config, [FromServices] IServiceProvider sp) =>
            {
                var manager = sp.GetService<Morpheo.Core.Configuration.MorpheoConfigManager>();
                if (manager == null) return Results.NotFound("Config Manager not active.");

                manager.Save(config);
                return Results.Ok();
            });

            // RESTART
            _app.MapPost(dashboardOptions.Path + "/api/sys/restart", ([FromServices] IHostApplicationLifetime lifetime) =>
            {
                // Trigger shutdown. The service manager (Docker/Systemd/Windows) will restart the process.
                lifetime.StopApplication();
                return Results.Ok("Stopping application...");
            });
        }

        await _app.StartAsync(ct);

        // Capture effective port if dynamic (0)
        var port = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault(0);
        if (port <= 0 || port > 65535)
        {
            throw new InvalidOperationException($"Failed to capture valid port from Kestrel. Got: {port}");
        }
        LocalPort = port;
    }

    /// <summary>
    /// Reports the friendly name of the EF Core provider backing the sync store,
    /// resolved from the parent DI container (where MorpheoDbContext is registered).
    /// </summary>
    private string ResolveDatabaseProvider()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetService<Morpheo.Core.Data.MorpheoDbContext>();
            var provider = db?.Database.ProviderName;
            if (string.IsNullOrEmpty(provider)) return "Unknown";

            // Map "Microsoft.EntityFrameworkCore.Sqlite" -> "Sqlite"
            return provider.Split('.').LastOrDefault() ?? provider;
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (_app != null)
        {
            try
            {
                await _app.StopAsync(ct);
            }
            finally
            {
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}

public class PrintRequestDto
{
    public string Content { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
}
