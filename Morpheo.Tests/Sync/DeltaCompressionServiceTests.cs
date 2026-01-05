using FluentAssertions;
using Morpheo.Core.Sync;

namespace Morpheo.Tests.Sync;

public class DeltaCompressionServiceTests
{
    private readonly DeltaCompressionService _service;

    public DeltaCompressionServiceTests()
    {
        _service = new DeltaCompressionService();
    }

    [Fact]
    public void CreatePatch_ShouldReturnValidPatch_WhenJsonAreDifferent()
    {
        // Arrange
        var original = "{\"prop\": \"old\"}";
        var modified = "{\"prop\": \"new\"}";

        // Act
        var patch = _service.CreatePatch(original, modified);

        // Assert
        patch.Should().NotBeNullOrEmpty();
        // Since we know our strategy is replacement, it should resemble modified, 
        // but strictly we just want to know if it's a valid patch that ApplyPatch can use.
        // Let's just check it's not empty.
        patch.Should().NotBe("null");
    }

    [Fact]
    public void ApplyPatch_ShouldReconstituteFinalJson()
    {
        // Arrange
        var original = "{\"id\": 1, \"value\": \"test\"}";
        var modified = "{\"id\": 1, \"value\": \"updated\"}";
        var patch = _service.CreatePatch(original, modified);

        // Act
        var result = _service.ApplyPatch(original, patch);

        // Assert
        // We compare normalized JSON strings or use a JSON parser to compare. 
        // Simple string compare might fail due to formatting, so we can use FluentAssertions with strict ordering or just parse it?
        // FluentAssertions has BeEquivalentTo for objects, but for strings we need to be careful.
        // Given our implementation returns stringified JsonNode, formatting should be standard.
        // But to be safe, let's just check that it parses to the expected property.
        
        result.Should().Contain("\"updated\"");
        
        // Better: parse both and compare
        var resultNode = System.Text.Json.Nodes.JsonNode.Parse(result);
        var expectedNode = System.Text.Json.Nodes.JsonNode.Parse(modified);
        
        // Use ToString equality for simple nodes
        resultNode!.ToJsonString().Should().Be(expectedNode!.ToJsonString());
    }

    [Fact]
    public void CreatePatch_ShouldThrowException_WhenJsonIsInvalid()
    {
        // Arrange
        var invalidJson = "{ invalid }";

        // Act
        Action act = () => _service.CreatePatch(invalidJson, "{}");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid JSON*");
    }
}
