using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Morpheo.Sdk.Blobs;

namespace Morpheo.Core.Blobs
{
    /// <summary>
    /// File system implementation of the blob store.
    /// Stores blobs as files and metadata as JSON sidecars.
    /// </summary>
    public class FileSystemBlobStore : IMorpheoBlobStore
    {
        private readonly string _rootPath;
        private const int BufferSize = 81920; // 80 KB

        public FileSystemBlobStore(IOptions<FileSystemBlobStoreOptions> options)
        {
            _rootPath = options?.Value?.RootPath ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_rootPath))
            {
                 throw new ArgumentException("MorpheoStorageOptions.RootPath cannot be null or empty.");
            }

            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
            }
        }

        /// <inheritdoc/>
        public async Task<string> SaveBlobAsync(Stream stream, string fileName, string contentType)
        {
            var blobId = Guid.NewGuid().ToString();
            var filePath = GetFilePath(blobId);

            long size = 0;
            // Write to disk with buffer
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                await stream.CopyToAsync(fileStream);
                size = fileStream.Length;
            }

            var metadata = new BlobMetadata
            {
                BlobId = blobId,
                FileName = fileName,
                ContentType = contentType,
                SizeBytes = size,
                Hash = string.Empty // Hash computation skipped for performance as per instructions focus on I/O
            };

            var metaPath = GetMetaPath(blobId);
            var json = JsonSerializer.Serialize(metadata);
            await File.WriteAllTextAsync(metaPath, json);

            return blobId;
        }

        /// <inheritdoc/>
        public Task<Stream?> GetBlobStreamAsync(string blobId)
        {
            var filePath = GetFilePath(blobId);
            if (!File.Exists(filePath))
            {
                return Task.FromResult<Stream?>(null);
            }
            
            // Return read-only stream
            return Task.FromResult<Stream?>(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        /// <inheritdoc/>
        public async Task<BlobMetadata?> GetBlobMetadataAsync(string blobId)
        {
            var metaPath = GetMetaPath(blobId);
            if (!File.Exists(metaPath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(metaPath);
                return JsonSerializer.Deserialize<BlobMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        private string GetFilePath(string blobId) => Path.Combine(_rootPath, blobId);
        private string GetMetaPath(string blobId) => Path.Combine(_rootPath, blobId + ".meta.json");
    }
}
