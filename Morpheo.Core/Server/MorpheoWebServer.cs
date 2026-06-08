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
        builder.Services.AddSingleton(sp => _serviceProvider.GetRequiredService<IRequestAuthenticator>());
        // IMorpheoBlobStore is optional — only registered if AddBlobStore() was called
        var blobStore = _serviceProvider.GetService<Morpheo.Sdk.Blobs.IMorpheoBlobStore>();
        if (blobStore != null)
            builder.Services.AddSingleton(blobStore);
        builder.Services.AddSingleton(sp => _options);

        builder.Services.AddSignalR();
        builder.Services.AddAuthorization();

        _app = builder.Build();

        // Morpheo Authentication Middleware
        _app.Use(async (context, next) =>
        {
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

        // Endpoint for Cold Sync history pull
        _app.MapGet("/api/sync/history", async ([FromQuery] long since, [FromServices] DataSyncService syncService) =>
        {
            var logs = await syncService.GetHistoryAsync(since);
            return Results.Ok(logs);
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
                    dbType = "Unknown" // Placeholder for now, could be improved by checking DbContext connection
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
