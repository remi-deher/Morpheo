using FluentAssertions;
using Morpheo.Core.Sync;

namespace Morpheo.Tests;

public class VectorClockTests
{
    [Fact]
    public void Increment_ShouldIncreaseLocalCounter()
    {
        // Arrange
        var clock = new VectorClock();
        string nodeId = "NodeA";

        // Act
        clock.Increment(nodeId);
        var state = clock.ToJson();

        // Assert
        state.Should().Contain("NodeA");
        // The JSON serialization of Dictionary<string, long> usually produces {"NodeA":1}
        state.Should().Contain("1"); 
    }
}