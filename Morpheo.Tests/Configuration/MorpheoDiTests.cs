using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Core;
using Morpheo.Core.Client;
using Morpheo.Core.Configuration;
using Morpheo.Sdk;

namespace Morpheo.Tests.Configuration;

public class MorpheoDiTests
{
    [Fact]
    public void AddMorpheo_ShouldRegisterRequiredServices_AndResolveRootObjects()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add Logging (Required by many Morpheo services)
        services.AddLogging(builder => builder.AddConsole());

        // Add Morpheo with minimal configuration
        services.AddMorpheo(builder =>
        {
            builder.Configure(options =>
            {
                options.NodeName = "TestNode";
                options.DiscoveryPort = 12345;
                // Avoid using real file system or network if possible by not specifying paths or keeping defaults that point to nowhere/null
                // But MorpheoNode needs actual implementations. 
                // In a unit test, we might want to override some with Mocks, but this is a Smoke Test for the DEFAULT extensibility.
                // However, AddMorpheo registers "NullNetworkDiscovery" and "NullMorpheoServer" by default if not overridden?
                // Looking at MorpheoServiceExtensions.cs: 
                // services.TryAddSingleton<INetworkDiscovery, NullNetworkDiscovery>();
                // services.TryAddSingleton<IMorpheoServer, NullMorpheoServer>();
                // So default usage is safe!
            });
        });

        // Act
        var provider = services.BuildServiceProvider();

        // Assert - Resolve Root Services
        var node = provider.GetService<MorpheoNode>();
        var client = provider.GetService<IMorpheoClient>();
        var options = provider.GetService<MorpheoOptions>();
        var hostedServices = provider.GetServices<IHostedService>();

        // 1. Root Objects existence
        node.Should().NotBeNull("MorpheoNode should be resolvable");
        client.Should().NotBeNull("IMorpheoClient should be resolvable");
        options.Should().NotBeNull("MorpheoOptions should be resolvable");

        // 2. Options correctness
        options!.NodeName.Should().Be("TestNode");
        options!.DiscoveryPort.Should().Be(12345);

        // 3. Hosted Service registration
        // MorpheoNode implements IHostedService but it is registered as "MorpheoNode" singleton in AddMorpheo line 44: services.AddSingleton<MorpheoNode>();
        // Wait, does it register it as IHostedService too? 
        // extensions.cs: services.AddSingleton<MorpheoNode>(); -> This only registers it as concrete type.
        // Usually we want services.AddHostedService<MorpheoNode>() if it is the main worker.
        // Let's check MorpheoServiceExtensions.cs again.
        // Line 44: services.AddSingleton<MorpheoNode>();
        // It does NOT seem to register it as IHostedService.
        // However, LogCompactionService IS registered as HostedService (Line 70).
        
        var compactor = hostedServices.FirstOrDefault(s => s.GetType().Name == "LogCompactionService");
        compactor.Should().NotBeNull("LogCompactionService should be registered as IHostedService");

        // 4. Singleton Scope Validation
        var client2 = provider.GetService<IMorpheoClient>();
        client.Should().BeSameAs(client2, "IMorpheoClient should be a singleton");

        var options2 = provider.GetService<MorpheoOptions>();
        options.Should().BeSameAs(options2, "MorpheoOptions should be a singleton");
    }
}
