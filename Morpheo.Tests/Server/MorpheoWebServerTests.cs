using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Morpheo.Core;
using Morpheo.Core.Configuration;
using Morpheo.Core.Data;
using Morpheo.Core.Extensions;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using Morpheo.Sdk;
using Xunit;
using Moq;

namespace Morpheo.Tests.Server;

public class MorpheoWebServerTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly MorpheoWebServer _server;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    public MorpheoWebServerTests()
    {
        var services = new ServiceCollection();

        // 1. Setup Core Dependencies
        services.AddLogging(logging => logging.AddConsole());
        
        // Options with Port 0 for dynamic port allocation
        var options = new MorpheoOptions 
        { 
            NodeName = "TestNode",
            DiscoveryPort = 0 // Let Kestrel choose a free port
        };
        services.AddSingleton(options);

        // Database (Sqlite File for persistence across requests)
        _dbPath = System.IO.Path.GetTempFileName();
        services.AddDbContext<MorpheoDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));
        
        // DataSync and its heavy dependencies
        services.AddSingleton<ConflictResolutionEngine>();
        services.AddSingleton<IEntityTypeResolver>(new SimpleTypeResolver());
        services.AddSingleton<DataSyncService>();
        services.AddSingleton<IMorpheoClient>(new Mock<IMorpheoClient>().Object);
        services.AddSingleton<ISyncRoutingStrategy>(new Mock<ISyncRoutingStrategy>().Object);

        // Discovery (Null implementation to keep it quiet)
        services.AddSingleton<Morpheo.Sdk.INetworkDiscovery>(new Morpheo.Core.Discovery.NullNetworkDiscovery());

        // Setup Dashboard Options
        services.AddSingleton(new DashboardOptions { Path = "/morpheo", Title = "Test Dashboard" });

        // Build Provider
        _serviceProvider = services.BuildServiceProvider();

        // Ensure DB created
        using (var scope = _serviceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MorpheoDbContext>().Database.EnsureCreated();
        }

        // 2. Instantiate Server
        _server = new MorpheoWebServer(options, _serviceProvider);
        _client = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync(CancellationToken.None);
        _client.Dispose();
        await _serviceProvider.DisposeAsync();
        
        if (System.IO.File.Exists(_dbPath))
        {
            try { System.IO.File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Dashboard_Should_Return_OK()
    {
        // Act: Start Server
        await _server.StartAsync(CancellationToken.None);
        
        var port = _server.LocalPort;
        port.Should().BeGreaterThan(0, "Server should bind to a real port");

        // Request Dashboard
        var response = await _client.GetAsync($"http://localhost:{port}/morpheo");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("<html"); // Basic check for HTML
        content.Should().Contain("Test Dashboard"); // Title injection check
    }

    [Fact]
    public async Task Metrics_Endpoint_Should_Return_Json()
    {
        // Act: Start Server
        await _server.StartAsync(CancellationToken.None);
        
        var port = _server.LocalPort;

        // Request Stats
        var response = await _client.GetAsync($"http://localhost:{port}/morpheo/api/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Contain("json");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("TestNode");
    }
}
