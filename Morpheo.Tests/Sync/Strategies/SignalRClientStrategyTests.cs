using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Tests.Sync.Strategies;

public class SignalRClientStrategyTests
{
    private readonly Mock<IHubConnectionWrapper> _connectionMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<SignalRClientStrategy>> _loggerMock;
    private readonly SignalRClientStrategy _strategy;

    public SignalRClientStrategyTests()
    {
        _connectionMock = new Mock<IHubConnectionWrapper>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<SignalRClientStrategy>>();

        _strategy = new SignalRClientStrategy(
            _connectionMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task PropagateAsync_ShouldInvokePushLog_WhenConnected()
    {
        // Arrange
        var log = new SyncLogDto("id", "entity", "Name", "{}", "UPDATE", 0, null!, "source");
        _connectionMock.Setup(c => c.State).Returns(HubConnectionState.Connected);

        // Act
        await _strategy.PropagateAsync(log, Enumerable.Empty<PeerInfo>(), null!);

        // Assert
        _connectionMock.Verify(c => c.InvokeAsync("PushLog", log, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PropagateAsync_ShouldNotInvoke_WhenDisconnected()
    {
        // Arrange
        var log = new SyncLogDto("id", "entity", "Name", "{}", "UPDATE", 0, null!, "source");
        _connectionMock.Setup(c => c.State).Returns(HubConnectionState.Disconnected);

        // Act
        await _strategy.PropagateAsync(log, Enumerable.Empty<PeerInfo>(), null!);

        // Assert
        _connectionMock.Verify(c => c.InvokeAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Constructor_ShouldStartConnection()
    {
        // Assert
        // The constructor fires ConnectAsync which calls StartAsync.
        // Since it's fire-and-forget, we might need a small delay or just verify valid call.
        // Ideally we should await something, but here we just check if it was called eventually.
        
        await Task.Delay(100); // Wait for async void task to start
        _connectionMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
