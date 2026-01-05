using System.Text.Json;
using MessagePack;
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
            serverOptions.ListenAnyIP(_options.DiscoveryPort); // Using DiscoveryPort for now based on options update
        });

        // Services
        builder.Services.AddSingleton(_serviceProvider.GetRequiredService<DataSyncService>());
        
        // Add Authorization if needed
        builder.Services.AddAuthorization();

        _app = builder.Build();

        // Morpheo Authentication Middleware
        // Morpheo Authentication Middleware
        _app.UseMiddleware<MorpheoAuthMiddleware>();

        // Endpoint for receiving logs (Sync Push)
        _app.MapPost("/morpheo/sync/push", async (HttpContext context, [FromServices] DataSyncService syncService) =>
        {
            SyncLogDto? dto = null;

            try 
            {
                if (context.Request.ContentType == "application/x-msgpack")
                {
                    dto = await MessagePackSerializer.DeserializeAsync<SyncLogDto>(context.Request.Body);
                }
                else 
                {
                    // Default to JSON
                    dto = await context.Request.ReadFromJsonAsync<SyncLogDto>();
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Deserialization failed: {ex.Message}");
            }

            if (dto == null) return Results.BadRequest("Null DTO");
            
            await syncService.ReceiveRemoteLogAsync(dto);
            return Results.Ok();
        });

        // Endpoint SignalR
        _app.MapHub<Morpheo.Core.SignalR.MorpheoSyncHub>("/morpheo/hub");

        // Endpoint for serving Blobs
        _app.MapGet("/morpheo/blobs/{id}", async (string id, [FromServices] Morpheo.Sdk.Blobs.IMorpheoBlobStore blobStore) =>
        {
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

            // -------------------------
            //     DASHBOARD (Custom vs Embedded)
            // -------------------------
            
            bool useCustomUi = !string.IsNullOrWhiteSpace(dashboardOptions.CustomUiPath) 
                               && Directory.Exists(dashboardOptions.CustomUiPath);

            if (useCustomUi)
            {
                var customPath = dashboardOptions.CustomUiPath!;
                // Serves static files from the custom folder (js, css, etc.)
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(customPath),
                    RequestPath = dashboardOptions.Path 
                });

                // Fallback for SPA routing (serving index.html)
                _app.MapGet(dashboardOptions.Path, async (HttpContext context) =>
                {
                    var indexPath = Path.Combine(customPath, "index.html");
                    if (File.Exists(indexPath))
                    {
                        var html = await File.ReadAllTextAsync(indexPath);
                        // Optional: Inject variables even in custom UI if markers exist
                        html = html.Replace("{{TITLE}}", dashboardOptions.Title)
                                   .Replace("{{PATH}}", dashboardOptions.Path);
                        return Results.Content(html, "text/html");
                    }
                    return Results.NotFound("Custom UI index.html not found.");
                });
            }
            else
            {
                // EMBEDDED UI
                
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
            }

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
        LocalPort = _app.Urls.Select(u => new Uri(u).Port).FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (_app != null) await _app.StopAsync(ct);
    }
}
