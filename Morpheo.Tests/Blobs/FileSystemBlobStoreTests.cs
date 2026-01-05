using System.Text;
using FluentAssertions;
using Morpheo.Core.Blobs;

namespace Morpheo.Tests.Blobs;

public class FileSystemBlobStoreTests : IDisposable
{
    private readonly string _testDir;

    public FileSystemBlobStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "MorpheoTests_Blobs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { /* ignored */ }
    }

    [Fact]
    public async Task PutAsync_ShouldCreateFileAndMetadata()
    {
        // Arrange
        var store = new FileSystemBlobStore(_testDir);
        var content = "Hello World Blob";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        // Act
        var blobId = await store.SaveBlobAsync(stream, "test.txt", "text/plain");

        // Assert
        blobId.Should().NotBeNullOrEmpty();
        
        var filePath = Path.Combine(_testDir, blobId);
        var metaPath = Path.Combine(_testDir, blobId + ".meta.json");

        File.Exists(filePath).Should().BeTrue();
        File.Exists(metaPath).Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ShouldRetrieveContentCurrently()
    {
        // Arrange
        var store = new FileSystemBlobStore(_testDir);
        var contentStr = "Important Data";
        var originalBytes = Encoding.UTF8.GetBytes(contentStr);
        
        using var stream = new MemoryStream(originalBytes);
        var blobId = await store.SaveBlobAsync(stream, "data.bin", "application/octet-stream");

        // Act
        using var retrievedStream = await store.GetBlobStreamAsync(blobId);
        
        // Assert
        retrievedStream.Should().NotBeNull();
        using var memoryStream = new MemoryStream();
        await retrievedStream!.CopyToAsync(memoryStream);
        
        var retrievedBytes = memoryStream.ToArray();
        retrievedBytes.Should().BeEquivalentTo(originalBytes);
    }

    [Fact]
    public async Task GetMetadata_ShouldRetrieveCorrectInfo()
    {
        // Arrange
        var store = new FileSystemBlobStore(_testDir);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12345")); // 5 bytes
        
        // Act
        var blobId = await store.SaveBlobAsync(stream, "numbers.txt", "text/plain");
        var metadata = await store.GetBlobMetadataAsync(blobId);

        // Assert
        metadata.Should().NotBeNull();
        metadata!.BlobId.Should().Be(blobId);
        metadata.FileName.Should().Be("numbers.txt");
        metadata.ContentType.Should().Be("text/plain");
        metadata.SizeBytes.Should().Be(5);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenBlobDoesNotExist()
    {
        // Arrange
        var store = new FileSystemBlobStore(_testDir);
        
        // Act
        var stream = await store.GetBlobStreamAsync("non-existent-id");
        var meta = await store.GetBlobMetadataAsync("non-existent-id");

        // Assert
        stream.Should().BeNull();
        meta.Should().BeNull();
    }
}
