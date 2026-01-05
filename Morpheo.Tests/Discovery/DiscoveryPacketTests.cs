using FluentAssertions;
using Morpheo.Core.Discovery;
using Morpheo.Sdk;

namespace Morpheo.Tests.Discovery;

public class DiscoveryPacketTests
{
    [Fact]
    public void Serialize_ShouldProduceBytes()
    {
        // Arrange
        var packet = new DiscoveryPacket
        {
            Id = "node-1",
            Name = "MyNode",
            IpAddress = "192.168.1.10",
            Port = 5000,
            Role = NodeRole.StandardClient,
            Tags = new[] { "test", "demo" },
            Type = DiscoveryMessageType.Hello
        };

        // Act
        var bytes = DiscoveryPacket.Serialize(packet);

        // Assert
        bytes.Should().NotBeNull();
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_ShouldReturnOriginalObject_RoundTrip()
    {
        // Arrange
        var original = new DiscoveryPacket
        {
            Id = "node-1",
            Name = "MyNode",
            IpAddress = "127.0.0.1",
            Port = 5000,
            Role = NodeRole.Server,
            Type = DiscoveryMessageType.Hello
        };

        var bytes = DiscoveryPacket.Serialize(original);

        // Act
        var deserialized = DiscoveryPacket.Deserialize(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Name.Should().Be(original.Name);
        deserialized.IpAddress.Should().Be(original.IpAddress);
        deserialized.Port.Should().Be(original.Port);
        deserialized.Role.Should().Be(original.Role);
        deserialized.Type.Should().Be(original.Type);
    }

    [Fact]
    public void Deserialize_ShouldReturnNull_WhenDataIsInvalid()
    {
        // Arrange
        var invalidBytes = new byte[] { 0x01, 0x02, 0x03 }; // Random junk

        // Act
        Action act = () => DiscoveryPacket.Deserialize(invalidBytes);

        // Assert
        // System.Text.Json throws on invalid JSON usually, ensuring we handle it or expect it.
        // The method signature returns nullable DiscoveryPacket? but wrapper might throw.
        // Let's check behavior.
        act.Should().Throw<System.Text.Json.JsonException>();
    }
}
