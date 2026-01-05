using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Morpheo.Core.Sync.Strategies;
using Morpheo.Sdk;

namespace Morpheo.Tests.Sync.Strategies;

public class HttpPushStrategyTests
{
    private readonly Mock<IHttpClientFactory> _clientFactoryMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly Mock<ILogger<HttpPushStrategy>> _loggerMock;
    private readonly HttpPushStrategy _strategy;
    private readonly Uri _targetUri = new("http://peer-ip");

    public HttpPushStrategyTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        
        var client = new HttpClient(_httpMessageHandlerMock.Object);
        client.BaseAddress = _targetUri;

        _clientFactoryMock = new Mock<IHttpClientFactory>();
        _clientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        
        _loggerMock = new Mock<ILogger<HttpPushStrategy>>();

        _strategy = new HttpPushStrategy(
            _clientFactoryMock.Object,
            _targetUri,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task PropagateAsync_ShouldSendPostRequest()
    {
        // Arrange
        var log = new SyncLogDto(
            Guid.NewGuid().ToString(),
            "entity-1",
            "TestEntity",
            "{}",
            "UPDATE",
            DateTime.UtcNow.Ticks,
            new Dictionary<string, long>(),
            "NodeA"
        );

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        await _strategy.PropagateAsync(log, Enumerable.Empty<PeerInfo>(), null!);

        // Assert
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/morpheo/sync/push") && // The strategy appends this path
                req.Content != null // Body presence check
            ),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task PropagateAsync_ShouldHandleHttpError_Gracefully()
    {
        // Arrange
        var log = new SyncLogDto(
             Guid.NewGuid().ToString(), "e1", "T", "{}", "U", 0, null!, "N"
        );

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError // 500
            });

        // Act
        Func<Task> act = async () => await _strategy.PropagateAsync(log, Enumerable.Empty<PeerInfo>(), null!);

        // Assert
        // Should not throw exception, just log error
        await act.Should().NotThrowAsync();
        
        // Verify logger was called
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)), 
            Times.Once);
    }
}
