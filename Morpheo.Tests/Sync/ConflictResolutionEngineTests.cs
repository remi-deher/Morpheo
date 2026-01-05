using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Morpheo.Core.Sync;
using Morpheo.Sdk;

namespace Morpheo.Tests.Sync;

public class ConflictResolutionEngineTests
{
    private readonly Mock<IEntityTypeResolver> _typeResolverMock;
    private readonly Mock<ILogger<ConflictResolutionEngine>> _loggerMock;
    private readonly ConflictResolutionEngine _engine;

    public ConflictResolutionEngineTests()
    {
        _typeResolverMock = new Mock<IEntityTypeResolver>();
        _loggerMock = new Mock<ILogger<ConflictResolutionEngine>>();
        _engine = new ConflictResolutionEngine(_typeResolverMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Resolve_ShouldUseLastWriteWins_WhenTimestampsAreDifferent()
    {
        // Arrange
        var localJson = "{\"value\": \"local\"}";
        var remoteJson = "{\"value\": \"remote\"}";
        long localTs = 100;
        long remoteTs = 200;

        // Act
        // Remote is newer
        var resultRemoteWins = _engine.Resolve("Entity", localJson, localTs, remoteJson, remoteTs);
        
        // Local is newer
        var resultLocalWins = _engine.Resolve("Entity", localJson, 200, remoteJson, 100);

        // Assert
        resultRemoteWins.Should().Be(remoteJson);
        resultLocalWins.Should().Be(localJson);
    }

    [Fact]
    public void Resolve_ShouldBeDeterministic_WhenTimestampsAreEqual()
    {
        // Arrange
        // We use two JSONs where one is lexicographically greater than the other.
        // "A" < "B"
        var jsonA = "{\"data\": \"A\"}";
        var jsonB = "{\"data\": \"B\"}";
        long ts = 100;

        // Act
        // local = A, remote = B. B > A. Expect B.
        var result1 = _engine.Resolve("Entity", jsonA, ts, jsonB, ts);
        
        // local = B, remote = A. B > A. Expect B.
        var result2 = _engine.Resolve("Entity", jsonB, ts, jsonA, ts);

        // Assert
        result1.Should().Be(jsonB);
        result2.Should().Be(jsonB);
        
        // Sanity check: they are equal
        result1.Should().Be(result2);
    }
}
