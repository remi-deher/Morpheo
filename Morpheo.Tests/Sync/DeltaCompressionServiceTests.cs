using FluentAssertions;
using System.Text.Json.Nodes;
using Morpheo.Core.Sync;

namespace Morpheo.Tests.Sync;

public class DeltaCompressionServiceTests
{
    private readonly DeltaCompressionService _service = new();

    // -------------------------------------------------------------------------
    //  CreatePatch — granularity
    // -------------------------------------------------------------------------

    [Fact]
    public void CreatePatch_ShouldReturnEmptyArray_WhenDocumentsAreIdentical()
    {
        var json = "{\"a\": 1, \"b\": \"hello\"}";
        var patch = _service.CreatePatch(json, json);
        var ops = JsonNode.Parse(patch)!.AsArray();
        ops.Should().BeEmpty();
    }

    [Fact]
    public void CreatePatch_ShouldProduceReplace_WhenScalarFieldChanges()
    {
        var original = "{\"name\": \"Alice\", \"age\": 30}";
        var modified = "{\"name\": \"Alice\", \"age\": 31}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().HaveCount(1);
        ops[0]["op"]!.GetValue<string>().Should().Be("replace");
        ops[0]["path"]!.GetValue<string>().Should().Be("/age");
        ops[0]["value"]!.GetValue<int>().Should().Be(31);
    }

    [Fact]
    public void CreatePatch_ShouldProduceAdd_WhenFieldIsAdded()
    {
        var original = "{\"a\": 1}";
        var modified = "{\"a\": 1, \"b\": \"new\"}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().ContainSingle(o =>
            o["op"]!.GetValue<string>() == "add" &&
            o["path"]!.GetValue<string>() == "/b" &&
            o["value"]!.GetValue<string>() == "new");
    }

    [Fact]
    public void CreatePatch_ShouldProduceRemove_WhenFieldIsRemoved()
    {
        var original = "{\"a\": 1, \"b\": \"old\"}";
        var modified = "{\"a\": 1}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().ContainSingle(o =>
            o["op"]!.GetValue<string>() == "remove" &&
            o["path"]!.GetValue<string>() == "/b");
    }

    [Fact]
    public void CreatePatch_ShouldNotTouchUnchangedFields()
    {
        var original = "{\"x\": 1, \"y\": 2, \"z\": 3}";
        var modified = "{\"x\": 1, \"y\": 99, \"z\": 3}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().HaveCount(1);
        ops[0]["path"]!.GetValue<string>().Should().Be("/y");
    }

    [Fact]
    public void CreatePatch_ShouldDiffNestedObjects()
    {
        var original = "{\"user\": {\"name\": \"Alice\", \"role\": \"admin\"}}";
        var modified = "{\"user\": {\"name\": \"Alice\", \"role\": \"viewer\"}}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().HaveCount(1);
        ops[0]["op"]!.GetValue<string>().Should().Be("replace");
        ops[0]["path"]!.GetValue<string>().Should().Be("/user/role");
        ops[0]["value"]!.GetValue<string>().Should().Be("viewer");
    }

    [Fact]
    public void CreatePatch_ShouldHandleArrayElementChange()
    {
        var original = "{\"tags\": [\"a\", \"b\", \"c\"]}";
        var modified = "{\"tags\": [\"a\", \"X\", \"c\"]}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().ContainSingle(o =>
            o["op"]!.GetValue<string>() == "replace" &&
            o["path"]!.GetValue<string>() == "/tags/1");
    }

    [Fact]
    public void CreatePatch_ShouldHandleArrayElementAdded()
    {
        var original = "{\"items\": [1, 2]}";
        var modified = "{\"items\": [1, 2, 3]}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().ContainSingle(o =>
            o["op"]!.GetValue<string>() == "add" &&
            o["path"]!.GetValue<string>() == "/items/-");
    }

    [Fact]
    public void CreatePatch_ShouldHandleArrayElementRemoved()
    {
        var original = "{\"items\": [1, 2, 3]}";
        var modified = "{\"items\": [1, 2]}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().ContainSingle(o =>
            o["op"]!.GetValue<string>() == "remove" &&
            o["path"]!.GetValue<string>() == "/items/2");
    }

    [Fact]
    public void CreatePatch_ShouldEscapeSlashAndTildeInKeys()
    {
        // RFC 6901: '/' → '~1', '~' → '~0'
        var original = "{}";
        var modified = "{\"a/b\": 1, \"c~d\": 2}";

        var ops = ParseOps(_service.CreatePatch(original, modified));

        ops.Should().Contain(o => o["path"]!.GetValue<string>() == "/a~1b");
        ops.Should().Contain(o => o["path"]!.GetValue<string>() == "/c~0d");
    }

    [Fact]
    public void CreatePatch_ShouldThrowException_WhenJsonIsInvalid()
    {
        Action act = () => _service.CreatePatch("{ invalid }", "{}");
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid JSON*");
    }

    // -------------------------------------------------------------------------
    //  ApplyPatch — round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyPatch_ShouldReconstituteFinalJson_AfterRoundTrip()
    {
        var original = "{\"id\": 1, \"value\": \"test\", \"count\": 5}";
        var modified = "{\"id\": 1, \"value\": \"updated\", \"count\": 5}";

        var patch  = _service.CreatePatch(original, modified);
        var result = _service.ApplyPatch(original, patch);

        JsonNode.DeepEquals(
            JsonNode.Parse(result),
            JsonNode.Parse(modified)
        ).Should().BeTrue();
    }

    [Fact]
    public void ApplyPatch_ShouldBeIdentity_WhenPatchIsEmptyArray()
    {
        var original = "{\"a\": 1}";
        var result   = _service.ApplyPatch(original, "[]");

        JsonNode.DeepEquals(
            JsonNode.Parse(result),
            JsonNode.Parse(original)
        ).Should().BeTrue();
    }

    [Fact]
    public void ApplyPatch_ShouldHandleAddRemoveReplace_Complex()
    {
        var original = "{\"a\": 1, \"b\": 2, \"c\": {\"x\": 10}}";
        var modified = "{\"a\": 99, \"c\": {\"x\": 10, \"y\": 20}, \"d\": \"new\"}";

        var patch  = _service.CreatePatch(original, modified);
        var result = _service.ApplyPatch(original, patch);

        JsonNode.DeepEquals(
            JsonNode.Parse(result),
            JsonNode.Parse(modified)
        ).Should().BeTrue();
    }

    [Fact]
    public void ApplyPatch_ShouldHandleArrayRoundTrip()
    {
        var original = "{\"tags\": [\"alpha\", \"beta\", \"gamma\"]}";
        var modified = "{\"tags\": [\"alpha\", \"DELTA\", \"gamma\", \"epsilon\"]}";

        var patch  = _service.CreatePatch(original, modified);
        var result = _service.ApplyPatch(original, patch);

        JsonNode.DeepEquals(
            JsonNode.Parse(result),
            JsonNode.Parse(modified)
        ).Should().BeTrue();
    }

    [Fact]
    public void ApplyPatch_ShouldThrowException_WhenJsonIsInvalid()
    {
        Action act = () => _service.ApplyPatch("{ bad }", "[]");
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid JSON*");
    }

    // -------------------------------------------------------------------------
    //  Helper
    // -------------------------------------------------------------------------

    private static List<JsonObject> ParseOps(string patch)
        => JsonNode.Parse(patch)!
                   .AsArray()
                   .Select(n => (JsonObject)n!)
                   .ToList();
}
