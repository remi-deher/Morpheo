using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Morpheo.Core.Client;
using Morpheo.Sdk;

namespace Morpheo.Tests.Client;

public class MorpheoHttpClientTests
{
    private readonly Mock<IHttpClientFactory> _clientFactoryMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly Mock<ILogger<MorpheoHttpClient>> _loggerMock;
    private readonly MorpheoOptions _options;
    private readonly MorpheoHttpClient _client;

    public MorpheoHttpClientTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _clientFactoryMock = new Mock<IHttpClientFactory>();
        _clientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        _loggerMock = new Mock<ILogger<MorpheoHttpClient>>();
        _options = new MorpheoOptions { NodeName = "TestNode", UseSecureConnection = false };

        _client = new MorpheoHttpClient(
            _clientFactoryMock.Object,
            _loggerMock.Object,
            _options
        );
    }

    [Fact]
    public async Task SendSyncUpdateAsync_ShouldUseCorrectUrlFormat()
    {
        // Arrange
        var target = new PeerInfo("id1", "Peer1", "10.0.0.5", 5555, NodeRole.StandardClient, Array.Empty<string>());
        var log = new SyncLogDto("id", "ent", "name", "{}", "ACT", 0, null!, "src");

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _client.SendSyncUpdateAsync(target, log);

        // Assert
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString() == "http://10.0.0.5:5555/api/sync"
            ),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task SendSyncUpdateAsync_ShouldUseHttps_WhenConfigured()
    {
        // Arrange
        _options.UseSecureConnection = true;
        var target = new PeerInfo("id1", "Peer1", "10.0.0.5", 5555, NodeRole.StandardClient, Array.Empty<string>());
        var log = new SyncLogDto("id", "ent", "name", "{}", "ACT", 0, null!, "src");

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _client.SendSyncUpdateAsync(target, log);

        // Assert
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.Scheme == "https"
            ),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldReturnEmptyList_OnFailure()
    {
        // Arrange
        var target = new PeerInfo("id1", "Peer1", "10.0.0.1", 5000, NodeRole.StandardClient, Array.Empty<string>());

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await _client.GetHistoryAsync(target, 0);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        // Logger should verify error
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cold Sync Failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), 
            Times.Once);
    }
}
