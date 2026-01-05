namespace Morpheo.Sdk.Blobs
{
    /// <summary>
    /// Represents metadata for a stored blob (Binary Large Object).
    /// </summary>
    public class BlobMetadata
    {
        /// <summary>
        /// Gets or sets the unique identifier of the blob.
        /// </summary>
        public string BlobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the original file name of the blob.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the MIME content type of the blob.
        /// </summary>
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the size of the blob in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the hash of the blob content for verification.
        /// </summary>
        public string Hash { get; set; } = string.Empty;
    }
}
