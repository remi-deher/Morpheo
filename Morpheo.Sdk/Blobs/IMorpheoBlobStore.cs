using System.IO;
using System.Threading.Tasks;

namespace Morpheo.Sdk.Blobs
{
    /// <summary>
    /// Defines the contract for a blob storage service.
    /// </summary>
    public interface IMorpheoBlobStore
    {
        /// <summary>
        /// Saves a blob asynchronously.
        /// </summary>
        /// <param name="stream">The stream containing the blob content.</param>
        /// <param name="fileName">The original name of the file.</param>
        /// <param name="contentType">The MIME type of the content.</param>
        /// <returns>The unique identifier (BlobId) of the stored blob.</returns>
        Task<string> SaveBlobAsync(Stream stream, string fileName, string contentType);

        /// <summary>
        /// Retrieves the content stream of a blob by its identifier.
        /// </summary>
        /// <param name="blobId">The unique identifier of the blob.</param>
        /// <returns>The stream of the blob content, or null if not found.</returns>
        Task<Stream?> GetBlobStreamAsync(string blobId);

        /// <summary>
        /// Retrieves the metadata of a blob by its identifier.
        /// </summary>
        /// <param name="blobId">The unique identifier of the blob.</param>
        /// <returns>The blob metadata, or null if not found.</returns>
        Task<BlobMetadata?> GetBlobMetadataAsync(string blobId);
    }
}
