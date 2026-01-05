using FluentAssertions;
using Morpheo.Core.Sync;

namespace Morpheo.Tests.Sync;

public class MerkleTreeServiceTests
{
    private readonly MerkleTreeService _service;

    public MerkleTreeServiceTests()
    {
        _service = new MerkleTreeService();
    }

    [Fact]
    public void ComputeRootHash_ShouldReturnSameHash_WhenDataIsIdentical()
    {
        // Arrange
        var data1 = new List<string> { "data1", "data2", "data3" };
        var data2 = new List<string> { "data1", "data2", "data3" };

        // Act
        var hash1 = _service.ComputeRootHash(data1);
        var hash2 = _service.ComputeRootHash(data2);

        // Assert
        hash1.Should().NotBeNullOrEmpty();
        hash1.Should().Be(hash2);
    }
    
    [Fact]
    public void ComputeRootHash_ShouldReturnSameHash_WhenDataOrderIsDifferent()
    {
        // Arrange (Testing our determinism feature)
        var data1 = new List<string> { "A", "B" };
        var data2 = new List<string> { "B", "A" };

        // Act
        var hash1 = _service.ComputeRootHash(data1);
        var hash2 = _service.ComputeRootHash(data2);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeRootHash_ShouldReturnDifferentHash_WhenDataIsAdded()
    {
        // Arrange
        var data = new List<string> { "data1", "data2" };
        var initialHash = _service.ComputeRootHash(data);

        // Act
        data.Add("data3");
        var newHash = _service.ComputeRootHash(data);

        // Assert
        newHash.Should().NotBe(initialHash);
    }
}
